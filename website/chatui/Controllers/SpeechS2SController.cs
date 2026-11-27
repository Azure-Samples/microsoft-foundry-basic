extern alias AzureIdentity;

using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Azure.Core;
using IdentityAuthenticationFailedException = AzureIdentity::Azure.Identity.AuthenticationFailedException;
using chatui.Auth;
using chatui.Configuration;
using chatui.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace chatui.Controllers;

/// <summary>
/// Bidirectional speech translation through parallel Foundry realtime sessions.
/// The primary session translates to the user's language and transcribes the
/// source. The reverse session translates the user's speech to English, or to
/// the last non-English interlocutor language that was detected.
/// </summary>
[ApiController]
[Route("api/speech")]
public class SpeechS2SController(
    IUserContext userContext,
    TokenCredential credential,
    IOptions<ChatApiOptions> chatOpts,
    TranslatorService translator,
    ILogger<SpeechS2SController> logger) : ControllerBase
{
    private static readonly string[] AoaiScopes = ["https://ai.azure.com/.default"];
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private const string TranslationDeployment = "gpt-realtime-translate";
    private const string TranscriptionDeployment = "gpt-live-transcribe";
    private static readonly TimeSpan TurnTranscriptPause = TimeSpan.FromMilliseconds(1100);

    [HttpGet("translate-live")]
    public async Task TranslateLive(
        [FromQuery] string targetLang = "en",
        CancellationToken ct = default)
    {
        _ = await userContext.GetCurrentAsync(ct).ConfigureAwait(false);

        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await HttpContext.Response.WriteAsync("WebSocket required", ct).ConfigureAwait(false);
            return;
        }

        using var clientWs = await HttpContext.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var clientSendGate = new SemaphoreSlim(1, 1);
        using var routingGate = new SemaphoreSlim(1, 1);
        var state = new TranslationState(
            clientWs,
            clientSendGate,
            routingGate,
            NormalizeLanguage(targetLang),
            lifetime);
        state.OutputPump = PumpClientOutputsAsync(state, lifetime.Token);

        try
        {
            var primary = await OpenUpstreamAsync(
                SessionRole.Primary,
                state.UserLanguage,
                enableTranscription: true,
                lifetime.Token).ConfigureAwait(false);
            state.SetPrimary(primary);
            primary.EventPump = PumpUpstreamEventsAsync(state, primary, lifetime.Token);

            if (!string.Equals(state.UserLanguage, "en", StringComparison.OrdinalIgnoreCase))
            {
                var reverse = await OpenUpstreamAsync(
                    SessionRole.Reverse,
                    "en",
                    enableTranscription: false,
                    lifetime.Token).ConfigureAwait(false);
                state.SetReverse(reverse);
                reverse.EventPump = PumpUpstreamEventsAsync(state, reverse, lifetime.Token);
            }

            await SendJsonAsync(clientWs, clientSendGate, new
            {
                type = "routing_updated",
                userLanguage = state.UserLanguage,
                replyLanguage = state.ReverseLanguage ?? state.UserLanguage,
            }, lifetime.Token).ConfigureAwait(false);

            // Client closure controls the lifetime. An old reverse event pump
            // ending during a language switch must not tear down the session.
            await PumpClientAudioAsync(state, lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is WebSocketException or IOException or SocketException
            or IdentityAuthenticationFailedException or Azure.RequestFailedException)
        {
            logger.LogError(ex, "Realtime translation session failed; required deployment: {Deployment}", TranslationDeployment);
            try
            {
                await SendJsonAsync(clientWs, clientSendGate, new
                {
                    type = "error",
                    message = "Realtime translation unavailable. Check the gpt-realtime-translate deployment, network connectivity, and managed-identity access.",
                }, ct).ConfigureAwait(false);
            }
            catch (WebSocketException sendException)
            {
                logger.LogDebug(sendException, "Could not report translation failure to disconnected client");
            }
        }
        finally
        {
            lifetime.Cancel();
            var sessions = state.GetSessions();
            foreach (var session in sessions)
            {
                session.Retire();
                await CloseUpstreamAsync(session).ConfigureAwait(false);
            }

            var eventPumps = sessions
                .Select(session => session.EventPump)
                .Where(task => task is not null)
                .Cast<Task>()
                .ToArray();
            if (eventPumps.Length > 0)
            {
                try { await Task.WhenAll(eventPumps).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (WebSocketException) { }
            }

            state.Outputs.Writer.TryComplete();
            if (state.OutputPump is not null)
            {
                try { await state.OutputPump.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (WebSocketException) { }
            }

            if (clientWs.State == WebSocketState.Open)
            {
                try
                {
                    await clientWs.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "done",
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (WebSocketException) { }
            }
        }
    }

    private async Task<UpstreamSession> OpenUpstreamAsync(
        SessionRole role,
        string language,
        bool enableTranscription,
        CancellationToken ct)
    {
        var accountBase = new Uri(chatOpts.Value.AIProjectEndpoint).GetLeftPart(UriPartial.Authority);
        var wsUrl = accountBase
            .Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase)
            + $"/openai/v1/realtime/translations?model={TranslationDeployment}";
        var accessToken = await credential
            .GetTokenAsync(new TokenRequestContext(AoaiScopes), ct)
            .ConfigureAwait(false);
        var ws = await ManualWebSocketConnectAsync(wsUrl, accessToken.Token, ct).ConfigureAwait(false);
        var session = new UpstreamSession(role, NormalizeLanguage(language), ws);

        object sessionConfig = enableTranscription
            ? new
            {
                type = "session.update",
                session = new
                {
                    audio = new
                    {
                        input = new
                        {
                            transcription = new { model = TranscriptionDeployment },
                        },
                        output = new { language = session.Language },
                    },
                },
            }
            : new
            {
                type = "session.update",
                session = new
                {
                    audio = new { output = new { language = session.Language } },
                },
            };

        await SendUpstreamJsonAsync(session, sessionConfig, ct).ConfigureAwait(false);
        logger.LogInformation(
            "Opened realtime {Role} translation session to {Language}; transcription={Transcription}",
            role,
            session.Language,
            enableTranscription);
        return session;
    }

    private async Task PumpClientAudioAsync(TranslationState state, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var frameCount = 0;
        while (!ct.IsCancellationRequested && state.Client.State == WebSocketState.Open)
        {
            var result = await state.Client
                .ReceiveAsync(new ArraySegment<byte>(buffer), ct)
                .ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) return;
            if (result.MessageType != WebSocketMessageType.Binary || result.Count == 0) continue;
            frameCount++;
            if (frameCount == 1)
            {
                logger.LogInformation(
                    "Received first client PCM frame ({Bytes} bytes); forwarding to {SessionCount} sessions",
                    result.Count,
                    state.GetSessions().Length);
            }

            var frame = new
            {
                type = "session.input_audio_buffer.append",
                audio = Convert.ToBase64String(buffer, 0, result.Count),
            };
            var sends = state.GetSessions()
                .Where(session => !session.IsRetired)
                .Select(session => SendUpstreamJsonAsync(session, frame, ct));
            await Task.WhenAll(sends).ConfigureAwait(false);
        }
    }

    private async Task PumpUpstreamEventsAsync(
        TranslationState state,
        UpstreamSession session,
        CancellationToken ct)
    {
        var buffer = new byte[65536];
        using var message = new MemoryStream();
        try
        {
            while (!ct.IsCancellationRequested
                && !session.IsRetired
                && session.Socket.State == WebSocketState.Open)
            {
                message.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await session.Socket
                        .ReceiveAsync(new ArraySegment<byte>(buffer), ct)
                        .ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    message.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);
                if (result.MessageType != WebSocketMessageType.Text) continue;

                using var doc = JsonDocument.Parse(
                    Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
                await HandleUpstreamEventAsync(state, session, doc.RootElement, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || session.IsRetired) { }
        catch (WebSocketException) when (session.IsRetired) { }
        catch (Exception ex) when (ex is WebSocketException or JsonException)
        {
            logger.LogWarning(
                ex,
                "Realtime {Role} translation event pump to {Language} failed",
                session.Role,
                session.Language);
            if (state.IsCurrent(session))
            {
                await TrySendClientErrorAsync(
                    state,
                    session.Language,
                    $"Translation session to {session.Language} stopped unexpectedly.",
                    ct).ConfigureAwait(false);
                state.Lifetime.Cancel();
            }
        }
    }

    private async Task HandleUpstreamEventAsync(
        TranslationState state,
        UpstreamSession session,
        JsonElement root,
        CancellationToken ct)
    {
        var eventType = root.TryGetProperty("type", out var typeNode) ? typeNode.GetString() : null;
        if (eventType is null) return;
        if (!eventType.EndsWith(".delta", StringComparison.Ordinal))
        {
            logger.LogInformation(
                "Realtime {Role} ({Language}) event: {EventType}",
                session.Role,
                session.Language,
                eventType);
        }

        switch (eventType)
        {
            case "input_audio_buffer.speech_started" when session.Role == SessionRole.Primary:
            case "session.input_audio_buffer.speech_started" when session.Role == SessionRole.Primary:
                if (state.EnsureSpeechTurn())
                {
                    await QueueClientJsonAsync(state, new
                    {
                        type = "turn_started",
                    }, ct).ConfigureAwait(false);
                }
                break;

            case "input_audio_buffer.speech_stopped" when session.Role == SessionRole.Primary:
            case "session.input_audio_buffer.speech_stopped" when session.Role == SessionRole.Primary:
                _ = FlushUnclassifiedTurnAfterTimeoutAsync(state, state.TurnVersion, ct);
                break;

            case "conversation.item.input_audio_transcription.delta" when session.Role == SessionRole.Primary:
            case "session.input_audio_transcription.delta" when session.Role == SessionRole.Primary:
            case "session.input_transcript.delta" when session.Role == SessionRole.Primary:
            {
                var text = GetText(root, "delta", "text");
                if (string.IsNullOrEmpty(text)) break;
                var itemId = GetText(root, "item_id", "itemId");
                var newTurn = state.EnsureTurn(itemId);
                if (newTurn)
                {
                    await QueueClientJsonAsync(state, new
                    {
                        type = "turn_started",
                    }, ct).ConfigureAwait(false);
                }
                var (turnVersion, revision) = state.AppendInputDelta(text);
                await QueueClientJsonAsync(state, new
                {
                    type = "original_delta",
                    text,
                }, ct).ConfigureAwait(false);
                _ = ClassifyCurrentTurnAfterPauseAsync(
                    state,
                    turnVersion,
                    revision,
                    ct);
                break;
            }

            case "conversation.item.input_audio_transcription.completed" when session.Role == SessionRole.Primary:
            case "session.input_audio_transcription.completed" when session.Role == SessionRole.Primary:
            case "session.input_transcript.done" when session.Role == SessionRole.Primary:
            {
                var text = GetText(root, "transcript", "text") ?? string.Empty;
                var itemId = GetText(root, "item_id", "itemId");
                var newTurn = state.EnsureTurn(itemId);
                if (newTurn)
                {
                    await QueueClientJsonAsync(state, new
                    {
                        type = "turn_started",
                    }, ct).ConfigureAwait(false);
                }
                var turnVersion = state.CompleteInputTranscript(text);
                await QueueClientJsonAsync(state, new
                {
                    type = "original_done",
                    text,
                }, ct).ConfigureAwait(false);
                await ClassifyTurnAsync(
                    state,
                    turnVersion,
                    GetText(root, "language"),
                    text,
                    ct).ConfigureAwait(false);
                break;
            }

            case "session.output_audio.delta":
            case "response.output_audio.delta":
            case "response.audio.delta":
            {
                var audio = GetText(root, "delta");
                if (string.IsNullOrEmpty(audio)) break;
                try
                {
                    var languageBytes = Encoding.UTF8.GetBytes(session.Language);
                    var audioBytes = Convert.FromBase64String(audio);
                    var framed = new byte[1 + languageBytes.Length + audioBytes.Length];
                    framed[0] = (byte)languageBytes.Length;
                    Buffer.BlockCopy(languageBytes, 0, framed, 1, languageBytes.Length);
                    Buffer.BlockCopy(audioBytes, 0, framed, 1 + languageBytes.Length, audioBytes.Length);
                    await RouteOutputAsync(
                        state,
                        session,
                        new BufferedOutput(WebSocketMessageType.Binary, framed),
                        ct).ConfigureAwait(false);
                }
                catch (FormatException ex)
                {
                    logger.LogWarning(ex, "Invalid base64 audio from translation session");
                }
                break;
            }

            case "session.output_audio_transcript.delta":
            case "session.output_transcript.delta":
            case "response.output_audio_transcript.delta":
            case "response.text.delta":
            case "response.audio_transcript.delta":
            {
                var text = GetText(root, "delta", "text");
                if (string.IsNullOrEmpty(text)) break;
                await RouteJsonOutputAsync(state, session, new
                {
                    type = "translation_delta",
                    lang = session.Language,
                    text,
                }, ct).ConfigureAwait(false);
                break;
            }

            case "session.output_audio_transcript.done":
            case "session.output_transcript.done":
            case "response.output_audio_transcript.done":
            case "response.text.done":
            case "response.audio_transcript.done":
                await RouteJsonOutputAsync(state, session, new
                {
                    type = "translation_done",
                    lang = session.Language,
                    text = GetText(root, "transcript", "text") ?? string.Empty,
                }, ct).ConfigureAwait(false);
                break;

            case "error":
            {
                var errorMessage = root.TryGetProperty("error", out var error)
                    && error.TryGetProperty("message", out var message)
                    ? message.GetString() ?? "Unknown upstream error"
                    : "Unknown upstream error";
                logger.LogWarning(
                    "Realtime {Role} translation ({Language}) upstream error: {Message}",
                    session.Role,
                    session.Language,
                    errorMessage);
                await TrySendClientErrorAsync(state, session.Language, errorMessage, ct)
                    .ConfigureAwait(false);
                break;
            }
        }
    }

    private async Task ClassifyTurnAsync(
        TranslationState state,
        long turnVersion,
        string? sourceLanguage,
        string transcript,
        CancellationToken ct)
    {
        lock (state.TurnGate)
        {
            if (state.TurnVersion != turnVersion || state.SelectedRole is not null) return;
        }

        if (string.IsNullOrWhiteSpace(sourceLanguage) && !string.IsNullOrWhiteSpace(transcript))
        {
            try
            {
                var detection = await translator
                    .TranslateAsync(transcript, state.UserLanguage, cancellationToken: ct)
                    .ConfigureAwait(false);
                sourceLanguage = detection.SourceLanguage;
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Could not detect source language for realtime turn");
                await TrySendClientErrorAsync(
                    state,
                    state.UserLanguage,
                    "Could not detect the speaker language; using translation to your language.",
                    ct).ConfigureAwait(false);
            }
        }

        sourceLanguage = NormalizeLanguage(sourceLanguage);
        var selectedRole = string.Equals(
            sourceLanguage,
            state.UserLanguage,
            StringComparison.OrdinalIgnoreCase)
            ? SessionRole.Reverse
            : SessionRole.Primary;
        if (!FlushSelectedOutput(state, turnVersion, selectedRole)) return;

        if (selectedRole == SessionRole.Primary && sourceLanguage != "unknown")
        {
            await EnsureReverseLanguageAsync(state, sourceLanguage, ct).ConfigureAwait(false);
        }
    }

    private async Task ClassifyCurrentTurnAfterPauseAsync(
        TranslationState state,
        long turnVersion,
        long revision,
        CancellationToken ct)
    {
        try
        {
            await Task.Delay(TurnTranscriptPause, ct).ConfigureAwait(false);
            var transcript = state.GetUnclassifiedTranscript(turnVersion, revision);
            if (string.IsNullOrWhiteSpace(transcript)) return;
            await ClassifyTurnAsync(
                state,
                turnVersion,
                sourceLanguage: null,
                transcript,
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private async Task EnsureReverseLanguageAsync(
        TranslationState state,
        string language,
        CancellationToken ct)
    {
        language = NormalizeLanguage(language);
        if (language == "unknown"
            || string.Equals(language, state.UserLanguage, StringComparison.OrdinalIgnoreCase)
            || string.Equals(language, state.ReverseLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await state.RoutingGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (string.Equals(language, state.ReverseLanguage, StringComparison.OrdinalIgnoreCase))
                return;

            var replacement = await OpenUpstreamAsync(
                SessionRole.Reverse,
                language,
                enableTranscription: false,
                ct).ConfigureAwait(false);
            var previous = state.ReplaceReverse(replacement);
            replacement.EventPump = PumpUpstreamEventsAsync(state, replacement, state.Lifetime.Token);

            await QueueClientJsonAsync(state, new
            {
                type = "routing_updated",
                userLanguage = state.UserLanguage,
                replyLanguage = language,
            }, ct).ConfigureAwait(false);

            if (previous is not null)
            {
                previous.Retire();
                await CloseUpstreamAsync(previous).ConfigureAwait(false);
            }
        }
        finally
        {
            state.RoutingGate.Release();
        }
    }

    private static Task RouteJsonOutputAsync<T>(
        TranslationState state,
        UpstreamSession session,
        T payload,
        CancellationToken ct) =>
        RouteOutputAsync(
            state,
            session,
            new BufferedOutput(
                WebSocketMessageType.Text,
                JsonSerializer.SerializeToUtf8Bytes(payload, JsonOpts)),
            ct);

    private static Task QueueClientJsonAsync<T>(
        TranslationState state,
        T payload,
        CancellationToken _)
    {
        state.Outputs.Writer.TryWrite(new BufferedOutput(
            WebSocketMessageType.Text,
            JsonSerializer.SerializeToUtf8Bytes(payload, JsonOpts)));
        return Task.CompletedTask;
    }

    private static Task RouteOutputAsync(
        TranslationState state,
        UpstreamSession session,
        BufferedOutput output,
        CancellationToken _)
    {
        lock (state.TurnGate)
        {
            if (!state.IsCurrent(session)) return Task.CompletedTask;
            if (!state.TurnOpen) state.BeginTurnUnsafe();

            if (state.SelectedRole is null)
                state.BufferFor(session.Role).Add(output);
            else if (state.SelectedRole == session.Role)
                state.Outputs.Writer.TryWrite(output);
        }
        return Task.CompletedTask;
    }

    private static bool FlushSelectedOutput(
        TranslationState state,
        long turnVersion,
        SessionRole selectedRole)
    {
        lock (state.TurnGate)
        {
            if (state.TurnVersion != turnVersion || state.SelectedRole is not null) return false;
            state.SelectedRole = selectedRole;
            foreach (var output in state.BufferFor(selectedRole))
                state.Outputs.Writer.TryWrite(output);
            state.PrimaryOutputs.Clear();
            state.ReverseOutputs.Clear();
            return true;
        }
    }

    private async Task FlushUnclassifiedTurnAfterTimeoutAsync(
        TranslationState state,
        long turnVersion,
        CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            lock (state.TurnGate)
            {
                if (state.TurnVersion != turnVersion || state.SelectedRole is not null) return;
                logger.LogWarning(
                    "Input transcription did not complete for realtime turn {Turn}; using primary translation",
                    turnVersion);
                state.SelectedRole = SessionRole.Primary;
                foreach (var output in state.PrimaryOutputs)
                    state.Outputs.Writer.TryWrite(output);
                state.PrimaryOutputs.Clear();
                state.ReverseOutputs.Clear();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private static async Task PumpClientOutputsAsync(
        TranslationState state,
        CancellationToken ct)
    {
        await foreach (var output in state.Outputs.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            await SendBufferedOutputAsync(state, output, ct).ConfigureAwait(false);
    }

    private static async Task SendBufferedOutputAsync(
        TranslationState state,
        BufferedOutput output,
        CancellationToken ct)
    {
        if (state.Client.State != WebSocketState.Open) return;
        await state.ClientSendGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (state.Client.State == WebSocketState.Open)
            {
                await state.Client.SendAsync(
                    new ArraySegment<byte>(output.Data),
                    output.MessageType,
                    true,
                    ct).ConfigureAwait(false);
            }
        }
        finally
        {
            state.ClientSendGate.Release();
        }
    }

    private static async Task SendUpstreamJsonAsync<T>(
        UpstreamSession session,
        T payload,
        CancellationToken ct)
    {
        if (session.IsRetired || session.Socket.State != WebSocketState.Open) return;
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOpts);
        await session.SendGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!session.IsRetired && session.Socket.State == WebSocketState.Open)
            {
                await session.Socket.SendAsync(
                    new ArraySegment<byte>(json),
                    WebSocketMessageType.Text,
                    true,
                    ct).ConfigureAwait(false);
            }
        }
        finally
        {
            session.SendGate.Release();
        }
    }

    private async Task TrySendClientErrorAsync(
        TranslationState state,
        string language,
        string message,
        CancellationToken ct)
    {
        try
        {
            await QueueClientJsonAsync(state, new
            {
                type = "error",
                lang = language,
                message,
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not forward realtime translation error to client");
        }
    }

    private static async Task CloseUpstreamAsync(UpstreamSession session)
    {
        await session.SendGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (session.Socket.State == WebSocketState.Open)
            {
                await session.Socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "done",
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException) { }
        finally
        {
            session.Socket.Dispose();
            session.SendGate.Release();
        }
    }

    private static string? GetText(JsonElement root, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (root.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }
        return null;
    }

    private static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return "unknown";
        var normalized = language.Trim().Replace('_', '-').ToLowerInvariant();
        var separator = normalized.IndexOf('-');
        return separator > 0 ? normalized[..separator] : normalized;
    }

    private enum SessionRole
    {
        Primary,
        Reverse,
    }

    private sealed class UpstreamSession(
        SessionRole role,
        string language,
        WebSocket socket)
    {
        private int _retired;

        public SessionRole Role { get; } = role;
        public string Language { get; } = language;
        public WebSocket Socket { get; } = socket;
        public SemaphoreSlim SendGate { get; } = new(1, 1);
        public Task? EventPump { get; set; }
        public bool IsRetired => Volatile.Read(ref _retired) == 1;
        public void Retire() => Interlocked.Exchange(ref _retired, 1);
    }

    private sealed class TranslationState(
        WebSocket client,
        SemaphoreSlim clientSendGate,
        SemaphoreSlim routingGate,
        string userLanguage,
        CancellationTokenSource lifetime)
    {
        private readonly object _sessionsGate = new();
        private UpstreamSession? _primary;
        private UpstreamSession? _reverse;

        public WebSocket Client { get; } = client;
        public SemaphoreSlim ClientSendGate { get; } = clientSendGate;
        public SemaphoreSlim RoutingGate { get; } = routingGate;
        public string UserLanguage { get; } = userLanguage;
        public CancellationTokenSource Lifetime { get; } = lifetime;
        public object TurnGate { get; } = new();
        public bool TurnOpen { get; private set; }
        public long TurnVersion { get; private set; }
        public long InputRevision { get; private set; }
        public string? CurrentInputItemId { get; private set; }
        public bool ClientTurnStarted { get; private set; }
        public StringBuilder InputTranscript { get; } = new();
        public SessionRole? SelectedRole { get; set; }
        public List<BufferedOutput> PrimaryOutputs { get; } = [];
        public List<BufferedOutput> ReverseOutputs { get; } = [];
        public Channel<BufferedOutput> Outputs { get; } =
            Channel.CreateUnbounded<BufferedOutput>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
            });
        public Task? OutputPump { get; set; }

        public string? ReverseLanguage
        {
            get
            {
                lock (_sessionsGate) return _reverse?.Language;
            }
        }

        public void SetPrimary(UpstreamSession session)
        {
            lock (_sessionsGate) _primary = session;
        }

        public void SetReverse(UpstreamSession session)
        {
            lock (_sessionsGate) _reverse = session;
        }

        public UpstreamSession? ReplaceReverse(UpstreamSession replacement)
        {
            lock (_sessionsGate)
            {
                var previous = _reverse;
                _reverse = replacement;
                return previous;
            }
        }

        public UpstreamSession[] GetSessions()
        {
            lock (_sessionsGate)
            {
                return new[] { _primary, _reverse }
                    .Where(session => session is not null)
                    .Cast<UpstreamSession>()
                    .ToArray();
            }
        }

        public bool IsCurrent(UpstreamSession session)
        {
            lock (_sessionsGate)
            {
                return ReferenceEquals(
                    session.Role == SessionRole.Primary ? _primary : _reverse,
                    session);
            }
        }

        public bool EnsureSpeechTurn()
        {
            lock (TurnGate)
            {
                if (!TurnOpen || SelectedRole is not null) BeginTurnUnsafe();
                if (ClientTurnStarted) return false;
                ClientTurnStarted = true;
                return true;
            }
        }

        public bool EnsureTurn(string? itemId)
        {
            lock (TurnGate)
            {
                var itemChanged = !string.IsNullOrEmpty(itemId)
                    && !string.IsNullOrEmpty(CurrentInputItemId)
                    && !string.Equals(CurrentInputItemId, itemId, StringComparison.Ordinal);
                // The translation endpoint exposes input transcript as one
                // continuous stream and may not send item boundaries. Once a
                // pause has classified the previous phrase, the next delta is
                // therefore the start of the next conversational turn.
                if (!TurnOpen || itemChanged || SelectedRole is not null)
                {
                    BeginTurnUnsafe();
                    CurrentInputItemId = itemId;
                }
                else if (string.IsNullOrEmpty(CurrentInputItemId)
                    && !string.IsNullOrEmpty(itemId))
                {
                    CurrentInputItemId = itemId;
                }

                if (ClientTurnStarted) return false;
                ClientTurnStarted = true;
                return true;
            }
        }

        public (long TurnVersion, long Revision) AppendInputDelta(string text)
        {
            lock (TurnGate)
            {
                InputTranscript.Append(text);
                InputRevision++;
                return (TurnVersion, InputRevision);
            }
        }

        public long CompleteInputTranscript(string text)
        {
            lock (TurnGate)
            {
                InputTranscript.Clear();
                InputTranscript.Append(text);
                InputRevision++;
                return TurnVersion;
            }
        }

        public string? GetUnclassifiedTranscript(long turnVersion, long revision)
        {
            lock (TurnGate)
            {
                return TurnVersion == turnVersion
                    && InputRevision == revision
                    && SelectedRole is null
                    ? InputTranscript.ToString()
                    : null;
            }
        }

        public void BeginTurnUnsafe()
        {
            TurnOpen = true;
            TurnVersion++;
            InputRevision = 0;
            CurrentInputItemId = null;
            ClientTurnStarted = false;
            InputTranscript.Clear();
            SelectedRole = null;
            PrimaryOutputs.Clear();
            ReverseOutputs.Clear();
        }

        public List<BufferedOutput> BufferFor(SessionRole role) =>
            role == SessionRole.Primary ? PrimaryOutputs : ReverseOutputs;
    }

    private sealed record BufferedOutput(WebSocketMessageType MessageType, byte[] Data);

    internal static async Task<WebSocket> ManualWebSocketConnectAsync(
        string wssUrl,
        string bearerToken,
        CancellationToken ct)
    {
        var uri = new Uri(wssUrl);
        var host = uri.Host;
        var port = uri.IsDefaultPort ? 443 : uri.Port;
        var pathAndQuery = uri.PathAndQuery;

        var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port, ct).ConfigureAwait(false);
        var netStream = tcp.GetStream();
        var ssl = new SslStream(netStream, false);
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = host,
        }, ct).ConfigureAwait(false);

        var keyBytes = new byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(keyBytes);
        var secKey = Convert.ToBase64String(keyBytes);

        var handshake =
            $"GET {pathAndQuery} HTTP/1.1\r\n" +
            $"Host: {host}\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Version: 13\r\n" +
            $"Sec-WebSocket-Key: {secKey}\r\n" +
            "User-Agent: ai-translator/1.0\r\n" +
            $"Authorization: Bearer {bearerToken}\r\n" +
            "\r\n";
        var handshakeBytes = Encoding.ASCII.GetBytes(handshake);
        await ssl.WriteAsync(handshakeBytes, ct).ConfigureAwait(false);
        await ssl.FlushAsync(ct).ConfigureAwait(false);

        var responseBuffer = new byte[4096];
        var responseLength = 0;
        while (responseLength < responseBuffer.Length)
        {
            var nextByte = new byte[1];
            var count = await ssl.ReadAsync(nextByte, ct).ConfigureAwait(false);
            if (count == 0) break;
            responseBuffer[responseLength++] = nextByte[0];
            if (responseLength >= 4
                && responseBuffer[responseLength - 4] == (byte)'\r'
                && responseBuffer[responseLength - 3] == (byte)'\n'
                && responseBuffer[responseLength - 2] == (byte)'\r'
                && responseBuffer[responseLength - 1] == (byte)'\n')
            {
                break;
            }
        }

        var responseText = Encoding.ASCII.GetString(responseBuffer, 0, responseLength);
        if (!responseText.StartsWith("HTTP/1.1 101", StringComparison.OrdinalIgnoreCase))
        {
            var body = string.Empty;
            try
            {
                var bodyBuffer = new byte[4096];
                using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                readCts.CancelAfter(TimeSpan.FromSeconds(2));
                var read = await ssl.ReadAsync(bodyBuffer, readCts.Token).ConfigureAwait(false);
                body = Encoding.UTF8.GetString(bodyBuffer, 0, read);
            }
            catch (OperationCanceledException) { }

            ssl.Dispose();
            tcp.Dispose();
            throw new WebSocketException(
                "Upstream returned non-101 handshake:\n" + responseText + "\n" + body);
        }

        return WebSocket.CreateFromStream(ssl, new WebSocketCreationOptions
        {
            IsServer = false,
            SubProtocol = null,
        });
    }

    private static async Task SendJsonAsync<T>(
        WebSocket ws,
        SemaphoreSlim gate,
        T payload,
        CancellationToken ct)
    {
        if (ws.State != WebSocketState.Open) return;
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOpts);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (ws.State == WebSocketState.Open)
            {
                await ws.SendAsync(
                    new ArraySegment<byte>(json),
                    WebSocketMessageType.Text,
                    true,
                    ct).ConfigureAwait(false);
            }
        }
        finally
        {
            gate.Release();
        }
    }
}
