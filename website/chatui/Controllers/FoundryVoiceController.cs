using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Azure.Core;
using chatui.Auth;
using chatui.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace chatui.Controllers;

[ApiController]
[Route("api/speech")]
public sealed class FoundryVoiceController(
    IUserContext userContext,
    TokenCredential credential,
    IOptions<ChatApiOptions> chatOptions,
    ILogger<FoundryVoiceController> logger) : ControllerBase
{
    private static readonly string[] FoundryScopes = ["https://ai.azure.com/.default"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet("voice-agent")]
    public async Task Transcribe([FromQuery] string langs = "en", CancellationToken ct = default)
    {
        _ = await userContext.GetCurrentAsync(ct).ConfigureAwait(false);

        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await HttpContext.Response.WriteAsync("WebSocket required", ct).ConfigureAwait(false);
            return;
        }

        var options = chatOptions.Value;
        if (!options.FoundryVoiceEnabled || string.IsNullOrWhiteSpace(options.FoundryVoiceAgentName))
        {
            HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            await HttpContext.Response.WriteAsync("Foundry Voice is not enabled", ct).ConfigureAwait(false);
            return;
        }

        using var client = await HttpContext.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        using var upstream = new ClientWebSocket();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var clientSendGate = new SemaphoreSlim(1, 1);

        try
        {
            var accessToken = await credential
                .GetTokenAsync(new TokenRequestContext(FoundryScopes), lifetime.Token)
                .ConfigureAwait(false);
            upstream.Options.SetRequestHeader("Authorization", $"Bearer {accessToken.Token}");
            upstream.Options.SetRequestHeader("Foundry-Features", "VoiceAgents=V1Preview");

            var endpoint = BuildWebSocketUri(
                options.AIProjectEndpoint,
                options.FoundryVoiceAgentName,
                Guid.NewGuid());
            await upstream.ConnectAsync(endpoint, lifetime.Token).ConfigureAwait(false);

            var primaryLanguage = ParsePrimaryLanguage(langs);
            var audioPump = PumpClientAudioAsync(client, upstream, lifetime.Token);
            var eventPump = PumpUpstreamEventsAsync(
                upstream,
                client,
                clientSendGate,
                primaryLanguage,
                lifetime.Token);

            await Task.WhenAny(audioPump, eventPump).ConfigureAwait(false);
            lifetime.Cancel();
            await AwaitPumpAsync(audioPump).ConfigureAwait(false);
            await AwaitPumpAsync(eventPump).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Managed Foundry Voice connection failed for agent {AgentName}", options.FoundryVoiceAgentName);
            await TrySendAsync(client, clientSendGate, new BrowserVoiceEvent(
                "error",
                Message: "Managed Voice is unavailable. Check the agent deployment and managed-identity access."),
                CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            lifetime.Cancel();
            await CloseSocketAsync(upstream).ConfigureAwait(false);
            await CloseSocketAsync(client).ConfigureAwait(false);
        }
    }

    internal static Uri BuildWebSocketUri(string projectEndpoint, string agentName, Guid sessionId)
    {
        var endpoint = new Uri(projectEndpoint.TrimEnd('/'));
        var builder = new UriBuilder(endpoint)
        {
            Scheme = endpoint.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? "wss" : "ws",
            Port = -1,
            Path = endpoint.AbsolutePath.TrimEnd('/')
                + $"/agents/{Uri.EscapeDataString(agentName)}/endpoint/protocols/voice",
            Query = $"api-version=v1&agent_session_id={sessionId:D}",
        };
        return builder.Uri;
    }

    internal static BrowserVoiceEvent? MapUpstreamEvent(JsonElement root, string defaultLanguage)
    {
        var eventType = root.TryGetProperty("type", out var typeElement)
            ? typeElement.GetString()
            : null;

        return eventType switch
        {
            "conversation.item.input_audio_transcription.delta" or
            "conversation.item.audio_transcription.delta" or
            "item.input_audio_transcription.delta" => MapDelta(root),

            "conversation.item.input_audio_transcription.completed" or
            "conversation.item.audio_transcription.completed" or
            "item.input_audio_transcription.completed" => MapCompleted(root, defaultLanguage),

            "error" => new BrowserVoiceEvent("error", Message: ReadError(root)),
            _ => null,
        };
    }

    private static BrowserVoiceEvent? MapDelta(JsonElement root)
    {
        var text = ReadString(root, "delta") ?? ReadString(root, "text");
        return string.IsNullOrEmpty(text)
            ? null
            : new BrowserVoiceEvent("transcribing", text, SpeakerId: ReadSpeaker(root));
    }

    private static BrowserVoiceEvent? MapCompleted(JsonElement root, string defaultLanguage)
    {
        var text = ReadString(root, "transcript") ?? ReadString(root, "text");
        return string.IsNullOrWhiteSpace(text)
            ? null
            : new BrowserVoiceEvent(
                "transcribed",
                text,
                ReadString(root, "language") ?? defaultLanguage,
                ReadSpeaker(root));
    }

    private static async Task PumpClientAudioAsync(
        WebSocket client,
        WebSocket upstream,
        CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        while (!ct.IsCancellationRequested && client.State == WebSocketState.Open)
        {
            var result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                return;
            if (result.MessageType != WebSocketMessageType.Binary || result.Count == 0)
                continue;

            var payload = JsonSerializer.SerializeToUtf8Bytes(new
            {
                type = "input_audio_buffer.append",
                audio = Convert.ToBase64String(buffer, 0, result.Count),
            }, JsonOptions);
            await upstream.SendAsync(payload, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }
    }

    private static async Task PumpUpstreamEventsAsync(
        WebSocket upstream,
        WebSocket client,
        SemaphoreSlim clientSendGate,
        string defaultLanguage,
        CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();

        while (!ct.IsCancellationRequested && upstream.State == WebSocketState.Open)
        {
            message.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await upstream.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;
                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text)
                continue;

            using var document = JsonDocument.Parse(message.GetBuffer().AsMemory(0, checked((int)message.Length)));
            var mapped = MapUpstreamEvent(document.RootElement, defaultLanguage);
            if (mapped is not null)
                await TrySendAsync(client, clientSendGate, mapped, ct).ConfigureAwait(false);
        }
    }

    private static async Task TrySendAsync<T>(
        WebSocket socket,
        SemaphoreSlim gate,
        T payload,
        CancellationToken ct)
    {
        if (socket.State != WebSocketState.Open)
            return;

        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (socket.State == WebSocketState.Open)
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }
        catch (WebSocketException) { }
        finally
        {
            gate.Release();
        }
    }

    private static async Task AwaitPumpAsync(Task pump)
    {
        try { await pump.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
    }

    private static async Task CloseSocketAsync(WebSocket socket)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
            return;
        try
        {
            await socket.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "done",
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (WebSocketException) { }
    }

    private static string ParsePrimaryLanguage(string raw)
    {
        var first = raw.Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "en";
        return first.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant() ?? "en";
    }

    private static string? ReadSpeaker(JsonElement root)
    {
        var speaker = ReadString(root, "speaker");
        if (speaker is not null)
            return speaker;
        return root.TryGetProperty("item", out var item) ? ReadString(item, "speaker") : null;
    }

    private static string ReadError(JsonElement root)
    {
        if (root.TryGetProperty("error", out var error))
            return ReadString(error, "message") ?? "Managed Voice returned an error.";
        return ReadString(root, "message") ?? "Managed Voice returned an error.";
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}

internal sealed record BrowserVoiceEvent(
    string Type,
    string? Text = null,
    string? Language = null,
    string? SpeakerId = null,
    string? Message = null);
