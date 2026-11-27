extern alias AzureIdentity;

using System.Net.Sockets;
using IdentityAuthenticationFailedException = AzureIdentity::Azure.Identity.AuthenticationFailedException;
using System.Net.WebSockets;
using System.Text.Json;
using Azure.Core;
using chatui.Configuration;
using chatui.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace chatui.Tests;

public sealed class SpeechTranslationFailureTests
{
    [Theory]
    [InlineData("websocket")]
    [InlineData("network")]
    [InlineData("io")]
    [InlineData("identity")]
    [InlineData("azure")]
    public async Task UpstreamStartupFailureIsReportedBeforeClosing(string failure)
    {
        using var socket = new RecordingSocket();
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpWebSocketFeature>(new AcceptingFeature(socket));
        var controller = new SpeechS2SController(
            new TestUserContext(), new FailingCredential(failure),
            Options.Create(new ChatApiOptions { AIProjectEndpoint = "https://foundry.example/api/projects/test" }),
            null!, NullLogger<SpeechS2SController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };

        await controller.TranslateLive("en", CancellationToken.None);

        var payload = Assert.Single(socket.Messages);
        using var document = JsonDocument.Parse(payload);
        Assert.Equal("error", document.RootElement.GetProperty("type").GetString());
        var message = document.RootElement.GetProperty("message").GetString();
        Assert.Contains("gpt-realtime-translate", message);
        Assert.DoesNotContain("private upstream detail", message);
        Assert.Equal(WebSocketState.Closed, socket.State);
    }

    private sealed class TestUserContext : chatui.Auth.IUserContext
    {
        public Task<chatui.Data.Entities.User> GetCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new chatui.Data.Entities.User
            {
                Id = Guid.NewGuid(),
                EntraObjectId = Guid.NewGuid().ToString(),
                DisplayName = "Test",
                Email = "test@example.invalid",
            });
    }

    private sealed class FailingCredential(string failure) : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw Failure();

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromException<AccessToken>(Failure());

        private Exception Failure() => failure switch
        {
            "network" => new SocketException(),
            "io" => new IOException("private upstream detail"),
            "identity" => new IdentityAuthenticationFailedException("private upstream detail"),
            "azure" => new Azure.RequestFailedException("private upstream detail"),
            _ => new WebSocketException("private upstream detail"),
        };
    }

    private sealed class AcceptingFeature(WebSocket socket) : IHttpWebSocketFeature
    {
        public bool IsWebSocketRequest => true;
        public Task<WebSocket> AcceptAsync(WebSocketAcceptContext context) => Task.FromResult(socket);
    }

    private sealed class RecordingSocket : WebSocket
    {
        private WebSocketState state = WebSocketState.Open;
        public List<byte[]> Messages { get; } = [];
        public override WebSocketState State => state;
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override string? SubProtocol => null;
        public override void Abort() => state = WebSocketState.Aborted;
        public override void Dispose() => state = WebSocketState.Closed;
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            state = WebSocketState.Closed;
            return Task.CompletedTask;
        }
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            CloseAsync(closeStatus, statusDescription, cancellationToken);
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Startup failed before audio capture.");
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            Assert.Equal(WebSocketState.Open, state);
            Messages.Add(buffer.ToArray());
            return Task.CompletedTask;
        }
    }
}
