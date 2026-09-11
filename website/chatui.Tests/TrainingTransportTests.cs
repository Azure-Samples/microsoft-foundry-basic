using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using chatui.Auth;
using chatui.Controllers;
using chatui.Data.Entities;
using chatui.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace chatui.Tests;

public sealed class TrainingTransportTests
{
    [Theory]
    [InlineData("en", true, false, false, "preserved", "acceptable", true)]
    [InlineData("ru", true, false, false, "preserved", "acceptable", false)]
    [InlineData("mixed", true, false, false, "preserved", "acceptable", false)]
    [InlineData("en", true, true, false, "preserved", "acceptable", false)]
    [InlineData("en", true, false, true, "preserved", "acceptable", false)]
    [InlineData("en", false, false, false, "preserved", "acceptable", false)]
    [InlineData("en", true, false, false, "changed", "acceptable", false)]
    [InlineData("en", true, false, false, "unknown", "notAssessable", false)]
    public void IndependentRecallRequiresUnaidedUneditedEnglishRetry(
        string language, bool retry, bool helped, bool edited, string meaning, string assessment, bool expected)
    {
        var result = new GuidedTrainingSession.CoachResult(
            "Could Friday work?", "Would Friday suit you?", "alreadyNatural", meaning,
            "notUsed", "Keep it concise.", false, language, assessment);

        Assert.Equal(expected, GuidedTrainingSession.IsIndependentRecall(result, retry, helped, edited));
    }

    [Fact]
    public void StructuredCoachingAcceptsAlternativeWordingWithoutExactMatching()
    {
        var result = GuidedTrainingSession.ParseCoachResult("""
            {"recognisedText":"Could Friday work?","naturalVersion":"Would Friday suit you?",
             "feedbackType":"conversationalAlternative","meaningPreserved":"preserved",
             "targetConstructionUsed":"notUsed","oneActionableTip":"Use a short, polite question.",
             "needsClarification":false,"language":"en","assessment":"acceptable"}
            """);

        Assert.True(GuidedTrainingSession.IsIndependentRecall(result, true, false, false));
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"assessment\":\"pass\"}")]
    public void InvalidCoachingNeverBecomesSuccess(string output) =>
        Assert.Throws<JsonException>(() => GuidedTrainingSession.ParseCoachResult(output));

    [Theory]
    [InlineData("({0})")]
    [InlineData("```json\n{0}\n```")]
    [InlineData("```\n{0}\n```")]
    public void CoachingAcceptsPresentationWrappersButStillValidatesSchema(string wrapper)
    {
        const string json = """
            {"recognisedText":"We discussed about the project.","naturalVersion":"We discussed the project.",
             "feedbackType":"correction","meaningPreserved":"preserved","targetConstructionUsed":"notAssessable",
             "oneActionableTip":"Drop about after discuss.","needsClarification":false,"language":"en","assessment":"needsWork"}
            """;

        var result = GuidedTrainingSession.ParseCoachResult(string.Format(wrapper, json));

        Assert.Equal("We discussed the project.", result.NaturalVersion);
        Assert.Throws<JsonException>(() => GuidedTrainingSession.ParseCoachResult(string.Format(wrapper, "{}")));
    }

    [Fact]
    public async Task NonMemberIsRejectedBeforeWebSocketOrCloudAccess()
    {
        using var services = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();
        using var body = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = body;
        var controller = new TrainingController(
            new SyntheticUserContext(), null!,
            Microsoft.Extensions.Options.Options.Create(new chatui.Configuration.ChatApiOptions()),
            NullLogger<TrainingController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };

        await controller.Live(CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        using var document = JsonDocument.Parse(body.ToArray());
        Assert.Equal("Training access is required.", document.RootElement.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("Could we move the call to Friday?", "Could we move the call to Friday?")]
    [InlineData("\u041c\u043e\u0436\u0435\u043c \u043f\u0435\u0440\u0435\u043d\u0435\u0441\u0442\u0438 \u0437\u0432\u043e\u043d\u043e\u043a \u043d\u0430 \u043f\u044f\u0442\u043d\u0438\u0446\u0443?", "Could we move the call to Friday?")]
    public async Task CompletedTurnForwardsSyntheticTranscriptAndCoachAudio(string original, string correction)
    {
        using var upstream = new ScriptedSocket(
            Text(new { type = "input_audio_buffer.speech_started" }),
            Text(new { type = "conversation.item.input_audio_transcription.delta", delta = original[..3] }),
            Text(new { type = "conversation.item.input_audio_transcription.completed", transcript = original }),
            Text(new { type = "response.output_audio_transcript.delta", delta = correction[..3] }),
            Text(new { type = "response.output_audio_transcript.done", transcript = correction }),
            Text(new { type = "response.output_audio.delta", delta = "AAABAA==" }));
        using var client = new ScriptedSocket();
        using var gate = new SemaphoreSlim(1, 1);

        await Controller().PumpEventsAsync(upstream, client, gate, CancellationToken.None);

        Assert.Equal(6, client.Sent.Count);
        AssertMessage(client.Sent[0], "turn_started");
        AssertMessage(client.Sent[1], "original_delta", original[..3]);
        AssertMessage(client.Sent[2], "original_done", original);
        AssertMessage(client.Sent[3], "correction_delta", correction[..3]);
        AssertMessage(client.Sent[4], "correction_done", correction);
        Assert.Equal(WebSocketMessageType.Binary, client.Sent[5].Type);
        Assert.Equal(new byte[] { 0, 0, 1, 0 }, client.Sent[5].Payload);
    }

    [Fact]
    public async Task ModelFailureProducesErrorNotSuccessfulCorrection()
    {
        using var upstream = new ScriptedSocket(Text(new
        {
            type = "error",
            error = new { message = "Synthetic service unavailable" },
        }));
        using var client = new ScriptedSocket();
        using var gate = new SemaphoreSlim(1, 1);

        await Controller().PumpEventsAsync(upstream, client, gate, CancellationToken.None);

        var message = Assert.Single(client.Sent);
        using var document = JsonDocument.Parse(message.Payload);
        Assert.Equal("error", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("Synthetic service unavailable", document.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task FragmentedUpstreamMessageIsReassembled()
    {
        var payload = Text(new { type = "conversation.item.input_audio_transcription.completed", transcript = "Synthetic phrase" }).Payload;
        using var upstream = new ScriptedSocket(
            new Frame(payload[..15], WebSocketMessageType.Text, false),
            new Frame(payload[15..], WebSocketMessageType.Text));
        using var client = new ScriptedSocket();
        using var gate = new SemaphoreSlim(1, 1);

        await Controller().PumpEventsAsync(upstream, client, gate, CancellationToken.None);

        AssertMessage(Assert.Single(client.Sent), "original_done", "Synthetic phrase");
    }

    [Fact]
    public async Task FreePracticeForwardsPcmAndIgnoresTextControls()
    {
        using var client = new ScriptedSocket(
            Text(new { type = "attempt.finish" }),
            new Frame([0, 0, 1, 0], WebSocketMessageType.Binary));
        using var upstream = new ScriptedSocket();
        using var gate = new SemaphoreSlim(1, 1);

        await TrainingController.PumpAudioAsync(client, upstream, gate, CancellationToken.None);

        using var document = JsonDocument.Parse(Assert.Single(upstream.Sent).Payload);
        Assert.Equal("input_audio_buffer.append", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("AAABAA==", document.RootElement.GetProperty("audio").GetString());
    }

    [Fact]
    public async Task CancelledPumpDoesNotForwardQueuedEvents()
    {
        using var upstream = new ScriptedSocket(Text(new { type = "input_audio_buffer.speech_started" }));
        using var client = new ScriptedSocket();
        using var gate = new SemaphoreSlim(1, 1);
        using var lifetime = new CancellationTokenSource();
        lifetime.Cancel();

        await Controller().PumpEventsAsync(upstream, client, gate, lifetime.Token);

        Assert.Empty(client.Sent);
    }

    [Fact]
    public async Task FreePracticeCommitsOnceAfterAllAudioAndRequestsOneResponse()
    {
        using var client = new ScriptedSocket(
            new Frame([0, 0, 1, 0], WebSocketMessageType.Binary),
            new Frame([2, 0, 3, 0], WebSocketMessageType.Binary),
            Text(new { type = "recording.finish" }),
            Text(new { type = "recording.finish" }),
            new Frame([4, 0], WebSocketMessageType.Binary));
        using var upstream = new ScriptedSocket();
        using var gate = new SemaphoreSlim(1, 1);

        await TrainingController.PumpAudioAsync(client, upstream, gate, CancellationToken.None);

        Assert.Equal(4, upstream.Sent.Count);
        AssertMessage(upstream.Sent[0], "input_audio_buffer.append");
        AssertMessage(upstream.Sent[1], "input_audio_buffer.append");
        AssertMessage(upstream.Sent[2], "input_audio_buffer.commit");
        AssertMessage(upstream.Sent[3], "response.create");
    }

    [Theory]
    [InlineData("completed", "response_done")]
    [InlineData("incomplete", "error")]
    [InlineData("failed", "error")]
    public async Task FreePracticeSignalsResponseCompletion(string status, string expected)
    {
        using var upstream = new ScriptedSocket(Text(new { type = "response.done", response = new { status } }));
        using var client = new ScriptedSocket();
        using var gate = new SemaphoreSlim(1, 1);

        await Controller().PumpEventsAsync(upstream, client, gate, CancellationToken.None);

        AssertMessage(Assert.Single(client.Sent), expected);
    }

    private static TrainingController Controller() =>
        new(null!, null!, null!, NullLogger<TrainingController>.Instance);

    [Fact]
    public async Task PrivateRelayPreservesTextAndBinaryFrames()
    {
        using var source = new ScriptedSocket(
            new Frame([1, 2], WebSocketMessageType.Binary, false),
            new Frame([3, 4], WebSocketMessageType.Binary),
            Text(new { type = "connected" }));
        using var target = new ScriptedSocket();

        await PrivateRelayController.PumpAsync(source, target, CancellationToken.None);

        Assert.Equal(3, target.Sent.Count);
        Assert.False(target.Sent[0].EndOfMessage);
        Assert.Equal(new byte[] { 3, 4 }, target.Sent[1].Payload);
        AssertMessage(target.Sent[2], "connected");
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("../tts/cognitiveservices/v1")]
    [InlineData("stt/speech/universal/v2/extra")]
    public async Task PrivateSpeechRelayRejectsUnlistedPaths(string path)
    {
        var controller = new PrivateRelayController(new SyntheticUserContext(), null!, null!, null!,
            NullLogger<PrivateRelayController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        await controller.Speech(path, CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, controller.Response.StatusCode);
    }

    private static Frame Text(object value) =>
        new(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)), WebSocketMessageType.Text);

    private static void AssertMessage(Frame frame, string type, string? text = null)
    {
        Assert.Equal(WebSocketMessageType.Text, frame.Type);
        using var document = JsonDocument.Parse(frame.Payload);
        Assert.Equal(type, document.RootElement.GetProperty("type").GetString());
        if (text is not null) Assert.Equal(text, document.RootElement.GetProperty("text").GetString());
    }

    private sealed record Frame(byte[] Payload, WebSocketMessageType Type, bool EndOfMessage = true);

    private sealed class SyntheticUserContext : IUserContext
    {
        public Task<User> GetCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new User
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                EntraObjectId = "00000000-0000-0000-0000-000000000002",
                DisplayName = "Synthetic non-member",
                Email = "non-member@example.invalid",
            });
    }

    private sealed class ScriptedSocket(params Frame[] frames) : WebSocket
    {
        private readonly Queue<Frame> incoming = new(frames);
        private WebSocketState state = WebSocketState.Open;
        public List<Frame> Sent { get; } = [];
        public override WebSocketCloseStatus? CloseStatus => state == WebSocketState.Open ? null : WebSocketCloseStatus.NormalClosure;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => state;
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
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!incoming.TryDequeue(out var frame))
            {
                state = WebSocketState.CloseReceived;
                return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
            }
            Assert.True(frame.Payload.Length <= buffer.Count, "Synthetic frame exceeds receive buffer.");
            frame.Payload.CopyTo(buffer.AsSpan());
            return Task.FromResult(new WebSocketReceiveResult(frame.Payload.Length, frame.Type, frame.EndOfMessage));
        }
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Sent.Add(new Frame(buffer.ToArray(), messageType, endOfMessage));
            return Task.CompletedTask;
        }
    }
}