using System.Net.WebSockets;
using Azure.Core;
using chatui.Auth;
using chatui.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace chatui.Controllers;

[ApiController]
[Route("api/relay")]
public sealed class PrivateRelayController(
    IUserContext userContext,
    TokenCredential credential,
    IOptions<SpeechOptions> speechOptions,
    IOptions<WebPubSubOptions> pubSubOptions,
    ILogger<PrivateRelayController> logger) : ControllerBase
{
    [HttpGet("speech/{**path}")]
    public async Task Speech(string path, CancellationToken cancellationToken)
    {
        _ = await userContext.GetCurrentAsync(cancellationToken);
        if (path is not ("stt/speech/universal/v2" or "s2s/speech/universal/v2"))
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        var options = speechOptions.Value;
        var token = await credential.GetTokenAsync(
            new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]), cancellationToken);
        var query = QueryString.Create(Request.Query.Where(pair =>
            !pair.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
            && !pair.Key.Equals("Ocp-Apim-Subscription-Key", StringComparison.OrdinalIgnoreCase)));
        var upstream = new UriBuilder(options.Endpoint)
        {
            Scheme = "wss", Port = -1, Path = path, Query = query.ToString().TrimStart('?'),
        };
        await RelayAsync(upstream.Uri, $"Bearer aad#{options.ResourceId}#{token.Token}", null, cancellationToken);
    }

    [HttpGet("pubsub")]
    public async Task PubSub(CancellationToken cancellationToken)
    {
        _ = await userContext.GetCurrentAsync(cancellationToken);
        var accessToken = Request.Query["access_token"].ToString();
        if (string.IsNullOrWhiteSpace(accessToken) || accessToken.Length > 16384)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        const string protocol = "json.webpubsub.azure.v1";
        if (!HttpContext.WebSockets.WebSocketRequestedProtocols.Contains(protocol))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        var options = pubSubOptions.Value;
        var upstream = new UriBuilder(options.Endpoint)
        {
            Scheme = "wss", Port = -1,
            Path = $"/client/hubs/{Uri.EscapeDataString(options.Hub)}",
            Query = QueryString.Create("access_token", accessToken).ToString().TrimStart('?'),
        };
        await RelayAsync(upstream.Uri, null, protocol, cancellationToken);
    }

    private async Task RelayAsync(Uri uri, string? authorization, string? protocol, CancellationToken cancellationToken)
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        if (Request.Headers.Origin.Count > 0
            && (!Uri.TryCreate(Request.Headers.Origin.ToString(), UriKind.Absolute, out var origin)
                || !string.Equals(origin.Authority, Request.Host.Value, StringComparison.OrdinalIgnoreCase)))
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(TimeSpan.FromHours(1));
        using var upstream = new ClientWebSocket();
        upstream.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        if (authorization is not null) upstream.Options.SetRequestHeader("Authorization", authorization);
        if (protocol is not null) upstream.Options.AddSubProtocol(protocol);
        try
        {
            using (var connect = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                connect.CancelAfter(TimeSpan.FromSeconds(20));
                await upstream.ConnectAsync(uri, connect.Token);
            }
            using var client = await HttpContext.WebSockets.AcceptWebSocketAsync(protocol);
            var upload = PumpAsync(client, upstream, lifetime.Token);
            var download = PumpAsync(upstream, client, lifetime.Token);
            try
            {
                await Task.WhenAny(upload, download);
            }
            finally
            {
                await lifetime.CancelAsync();
                client.Abort();
                upstream.Abort();
            }
            await Task.WhenAll(upload, download);
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or IOException)
        {
            logger.LogDebug("Private relay ended ({ErrorType})", exception.GetType().Name);
            if (!Response.HasStarted) Response.StatusCode = StatusCodes.Status502BadGateway;
        }
    }

    internal static async Task PumpAsync(WebSocket source, WebSocket target, CancellationToken cancellationToken)
    {
        var buffer = new byte[16384];
        var messageBytes = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var message = await source.ReceiveAsync(buffer.AsMemory(), cancellationToken);
            if (message.MessageType == WebSocketMessageType.Close) return;
            messageBytes += message.Count;
            if (messageBytes > 1024 * 1024) throw new IOException("Relay message limit exceeded.");
            await target.SendAsync(buffer.AsMemory(0, message.Count), message.MessageType, message.EndOfMessage, cancellationToken);
            if (message.EndOfMessage) messageBytes = 0;
        }
    }
}