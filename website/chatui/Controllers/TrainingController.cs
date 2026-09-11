using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Azure.Core;
using chatui.Auth;
using chatui.Configuration;
using chatui.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace chatui.Controllers;

[ApiController]
[Route("api/training")]
public sealed class TrainingController(
    IUserContext userContext,
    TokenCredential credential,
    IOptions<ChatApiOptions> chatOptions,
    ILogger<TrainingController> logger) : ControllerBase
{
    private const string Deployment = "gpt-realtime-2.1";
    private static readonly string[] Scopes = ["https://ai.azure.com/.default"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet("live")]
    public async Task Live(CancellationToken cancellationToken)
    {
        var user = await userContext.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (!TrainingAccess.IsMember(user, chatOptions.Value.TrainingMemberObjectIds))
        {
            HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            await HttpContext.Response.WriteAsJsonAsync(
                new { error = "Training access is required." },
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var mode = Request.Query["mode"].ToString();
        var guided = mode == "guided";
        if (mode.Length > 0 && mode != "free" && !guided)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        if (guided && !chatOptions.Value.TrainingGuidedEnabled)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        if (guided && Request.Query["protocol"] != "guided.v1")
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var client = await HttpContext.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        var accountBase = new Uri(chatOptions.Value.AIProjectEndpoint).GetLeftPart(UriPartial.Authority);
        var upstreamUrl = accountBase.Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase)
            + $"/openai/v1/realtime?model={Deployment}";
        var token = await credential.GetTokenAsync(
            new TokenRequestContext(Scopes),
            cancellationToken).ConfigureAwait(false);

        using var upstream = await SpeechS2SController.ManualWebSocketConnectAsync(
            upstreamUrl,
            token.Token,
            cancellationToken).ConfigureAwait(false);

        if (guided)
        {
            await new GuidedTrainingSession().RunAsync(client, upstream, cancellationToken).ConfigureAwait(false);
            return;
        }

        await SendJsonAsync(upstream, new
        {
            type = "session.update",
            session = new
            {
                type = "realtime",
                instructions = "You are a professional spoken-English translator and coach. Detect the language of each completed utterance. "
                    + "If the user speaks a language other than English, translate it into accurate, natural, grammatically correct English. "
                    + "If the user speaks English, improve grammar or unnatural phrasing only when needed; otherwise repeat it unchanged. "
                    + "In both cases preserve the exact meaning, names, numbers, terminology, level of formality, tone, emotion, and speaking style. "
                    + "Do not summarize, embellish, explain, answer, or add information. Output only the resulting English utterance.",
                output_modalities = new[] { "audio" },
                audio = new
                {
                    input = new
                    {
                        format = new { type = "audio/pcm", rate = 24000 },
                        transcription = new { model = "gpt-live-transcribe" },
                        turn_detection = (object?)null,
                    },
                    output = new
                    {
                        voice = "alloy",
                        format = new { type = "audio/pcm", rate = 24000 },
                    },
                },
            },
        }, cancellationToken).ConfigureAwait(false);

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var clientSendGate = new SemaphoreSlim(1, 1);
        var upstreamSendGate = new SemaphoreSlim(1, 1);
        var audioPump = PumpAudioAsync(client, upstream, upstreamSendGate, lifetime.Token);
        var eventPump = PumpEventsAsync(upstream, client, clientSendGate, lifetime.Token);

        try
        {
            await Task.WhenAny(audioPump, eventPump).ConfigureAwait(false);
        }
        finally
        {
            lifetime.Cancel();
            await CloseAsync(upstream).ConfigureAwait(false);
            await CloseAsync(client).ConfigureAwait(false);
            try { await Task.WhenAll(audioPump, eventPump).ConfigureAwait(false); }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException) { }
        }
    }

    internal static async Task PumpAudioAsync(
        WebSocket client,
        WebSocket upstream,
        SemaphoreSlim sendGate,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var finished = false;
        using var control = new MemoryStream();
        while (!cancellationToken.IsCancellationRequested && client.State == WebSocketState.Open)
        {
            var result = await client.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) return;
            if (finished) continue;
            if (result.MessageType == WebSocketMessageType.Text)
            {
                if (control.Length + result.Count > 1024) throw new InvalidDataException();
                control.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;
                using var document = JsonDocument.Parse(control.ToArray());
                control.SetLength(0);
                if (ReadString(document.RootElement, "type") != "recording.finish") continue;
                finished = true;
                await sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await SendJsonAsync(upstream, new { type = "input_audio_buffer.commit" }, cancellationToken).ConfigureAwait(false);
                    await SendJsonAsync(upstream, new { type = "response.create" }, cancellationToken).ConfigureAwait(false);
                }
                finally { sendGate.Release(); }
                continue;
            }
            if (result.MessageType != WebSocketMessageType.Binary || result.Count == 0) continue;
            await sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await SendJsonAsync(upstream, new
                {
                    type = "input_audio_buffer.append",
                    audio = Convert.ToBase64String(buffer, 0, result.Count),
                }, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                sendGate.Release();
            }
        }
    }

    internal async Task PumpEventsAsync(
        WebSocket upstream,
        WebSocket client,
        SemaphoreSlim sendGate,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[16384];
        using var message = new MemoryStream();
        while (!cancellationToken.IsCancellationRequested && upstream.State == WebSocketState.Open)
        {
            message.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await upstream.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) return;
                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);
            if (result.MessageType != WebSocketMessageType.Text) continue;

            using var document = JsonDocument.Parse(
                new ReadOnlyMemory<byte>(message.GetBuffer(), 0, (int)message.Length));
            var root = document.RootElement;
            var eventType = root.TryGetProperty("type", out var type) ? type.GetString() : null;
            switch (eventType)
            {
                case "input_audio_buffer.speech_started":
                    await SendClientJsonAsync(client, sendGate, new { type = "turn_started" }, cancellationToken).ConfigureAwait(false);
                    break;
                case "conversation.item.input_audio_transcription.delta":
                    await SendClientJsonAsync(client, sendGate, new
                    {
                        type = "original_delta",
                        text = ReadString(root, "delta"),
                    }, cancellationToken).ConfigureAwait(false);
                    break;
                case "conversation.item.input_audio_transcription.completed":
                    await SendClientJsonAsync(client, sendGate, new
                    {
                        type = "original_done",
                        text = ReadString(root, "transcript"),
                    }, cancellationToken).ConfigureAwait(false);
                    break;
                case "response.output_audio_transcript.delta":
                    await SendClientJsonAsync(client, sendGate, new
                    {
                        type = "correction_delta",
                        text = ReadString(root, "delta"),
                    }, cancellationToken).ConfigureAwait(false);
                    break;
                case "response.output_audio_transcript.done":
                    await SendClientJsonAsync(client, sendGate, new
                    {
                        type = "correction_done",
                        text = ReadString(root, "transcript"),
                    }, cancellationToken).ConfigureAwait(false);
                    break;
                case "response.output_audio.delta":
                    var audio = ReadString(root, "delta");
                    if (!string.IsNullOrEmpty(audio))
                    {
                        await SendClientAsync(
                            client,
                            sendGate,
                            Convert.FromBase64String(audio),
                            WebSocketMessageType.Binary,
                            cancellationToken).ConfigureAwait(false);
                    }
                    break;
                case "response.done":
                    var response = root.GetProperty("response");
                    if (ReadString(response, "status") == "completed")
                        await SendClientJsonAsync(client, sendGate, new { type = "response_done" }, cancellationToken).ConfigureAwait(false);
                    else
                        await SendClientJsonAsync(client, sendGate, new { type = "error", message = "The complete recording could not be translated. Please try again." }, cancellationToken).ConfigureAwait(false);
                    break;
                case "conversation.item.input_audio_transcription.failed":
                case "error":
                    var error = root.TryGetProperty("error", out var errorNode)
                        ? ReadString(errorNode, "message")
                        : "Unknown realtime error";
                    logger.LogWarning("Training realtime error: {Error}", error);
                    await SendClientJsonAsync(client, sendGate, new { type = "error", message = error }, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
    }

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static Task SendJsonAsync(WebSocket socket, object value, CancellationToken cancellationToken) =>
        socket.SendAsync(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, JsonOptions)),
            WebSocketMessageType.Text,
            true,
            cancellationToken);

    private static async Task SendClientJsonAsync(
        WebSocket socket,
        SemaphoreSlim sendGate,
        object value,
        CancellationToken cancellationToken) =>
        await SendClientAsync(
            socket,
            sendGate,
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, JsonOptions)),
            WebSocketMessageType.Text,
            cancellationToken).ConfigureAwait(false);

    private static async Task SendClientAsync(
        WebSocket socket,
        SemaphoreSlim sendGate,
        ReadOnlyMemory<byte> payload,
        WebSocketMessageType messageType,
        CancellationToken cancellationToken)
    {
        await sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(payload, messageType, true, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            sendGate.Release();
        }
    }

    private static async Task CloseAsync(WebSocket socket)
    {
        if (socket.State != WebSocketState.Open) return;
        try
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
        }
        catch (WebSocketException) { }
    }
}