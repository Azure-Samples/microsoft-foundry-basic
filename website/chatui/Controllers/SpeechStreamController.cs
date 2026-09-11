using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Azure.Core;
using chatui.Auth;
using chatui.Configuration;
using chatui.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech.Transcription;
using Microsoft.Extensions.Options;

namespace chatui.Controllers;

/// <summary>
/// Server-side conversation transcription with speaker diarization + continuous
/// LID. The browser opens a WebSocket to <c>/api/speech/transcribe</c>, streams
/// raw 16-kHz mono 16-bit PCM chunks up, and receives back JSON messages of the
/// shape <c>{ type: "transcribed", speakerId, text, language }</c>.
///
/// This unlocks two things the browser-side SpeechRecognizer cannot do:
///  * Diarization — automatic Speaker 1 / Speaker 2 labelling for overlapping
///    or alternating voices from the same microphone.
///  * Better noise robustness — the server-side ConversationTranscriber model
///    is larger than what the JS SDK ships with.
///
/// Translation + softening are handled client-side per turn using the existing
/// /api/translate endpoint, so this controller is scoped purely to STT.
/// </summary>
[ApiController]
[Route("api/speech")]
public class SpeechStreamController(
    IUserContext userContext,
    TokenCredential credential,
    IOptions<SpeechOptions> speechOptions,
    ILogger<SpeechStreamController> logger) : ControllerBase
{
    private static readonly string[] CognitiveServicesScopes =
        ["https://cognitiveservices.azure.com/.default"];

    [HttpGet("transcribe")]
    public async Task Transcribe(
        [FromQuery] string langs = "en-US",
        CancellationToken ct = default)
    {
        // Authorise the request via the same DevAuth / EasyAuth flow used
        // for /api/me. This also drives the "created new user" side-effect.
        _ = await userContext.GetCurrentAsync(ct).ConfigureAwait(false);

        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await HttpContext.Response.WriteAsync("WebSocket required", ct).ConfigureAwait(false);
            return;
        }

        using var ws = await HttpContext.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);

        var opts = speechOptions.Value;
        var token = await credential.GetTokenAsync(new TokenRequestContext(CognitiveServicesScopes), ct).ConfigureAwait(false);
        var speechCfg = SpeechConfig.FromEndpoint(new UriBuilder(opts.Endpoint) { Scheme = "wss", Port = -1 }.Uri);
        speechCfg.AuthorizationToken = $"aad#{opts.ResourceId}#{token.Token}";
        speechCfg.SetProperty(PropertyId.SpeechServiceConnection_LanguageIdMode, "Continuous");
        // Bias the continuous LID for accuracy over latency — matches what the
        // browser code was doing before the migration.
        speechCfg.SetProperty("SpeechServiceConnection_ContinuousLanguageIdPriority", "Accuracy");
        // Longer end-of-utterance silence timeout so long thoughts aren't split.
        speechCfg.SetProperty(PropertyId.Speech_SegmentationSilenceTimeoutMs, "1200");
        speechCfg.SetProfanity(ProfanityOption.Raw);

        var candidateLanguages = ParseCandidateLanguages(langs);
        var autoDetect = AutoDetectSourceLanguageConfig.FromLanguages(candidateLanguages.ToArray());

        var pushStream = AudioInputStream.CreatePushStream(
            AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1));
        var audioCfg = AudioConfig.FromStreamInput(pushStream);

        using var transcriber = new ConversationTranscriber(speechCfg, autoDetect, audioCfg);

        var sendLock = new SemaphoreSlim(1, 1);

        transcriber.Transcribing += (_, e) =>
            _ = SendJsonAsync(ws, sendLock, new
            {
                type = "transcribing",
                speakerId = e.Result.SpeakerId ?? "?",
                text = e.Result.Text,
            }, ct);

        transcriber.Transcribed += (_, e) =>
        {
            if (e.Result.Reason != ResultReason.RecognizedSpeech || string.IsNullOrWhiteSpace(e.Result.Text))
                return;
            var lidResult = AutoDetectSourceLanguageResult.FromResult(e.Result);
            _ = SendJsonAsync(ws, sendLock, new
            {
                type = "transcribed",
                speakerId = e.Result.SpeakerId ?? "?",
                text = e.Result.Text,
                language = lidResult?.Language ?? "",
            }, ct);
        };

        transcriber.Canceled += (_, e) =>
        {
            // EndOfStream is normal — happens whenever the Speech service
            // decides to end the current session (silence timeout, session
            // rotation, etc.). Silently restart in-place instead of killing
            // the WebSocket — the client would otherwise see the mic "stop
            // hearing" mid-conversation.
            if (e.Reason == CancellationReason.EndOfStream)
            {
                logger.LogInformation("ConversationTranscriber EndOfStream — auto-restarting in-place.");
                _ = Task.Run(async () =>
                {
                    try { await transcriber.StopTranscribingAsync().ConfigureAwait(false); } catch { }
                    try { await transcriber.StartTranscribingAsync().ConfigureAwait(false); } catch (Exception ex) { logger.LogWarning(ex, "Auto-restart failed"); }
                });
                return;
            }
            logger.LogWarning("ConversationTranscriber canceled: {Reason} {Details}", e.Reason, e.ErrorDetails);
            _ = SendJsonAsync(ws, sendLock, new
            {
                type = "canceled",
                reason = e.Reason.ToString(),
                errorDetails = e.ErrorDetails,
            }, ct);
        };

        try
        {
            await transcriber.StartTranscribingAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start transcription");
            await SendJsonAsync(ws, sendLock, new { type = "error", message = ex.Message }, ct)
                .ConfigureAwait(false);
            await ws.CloseAsync(WebSocketCloseStatus.InternalServerError, "startup failure", ct)
                .ConfigureAwait(false);
            return;
        }

        // Pump audio bytes from the client into the SDK's push stream.
        var buf = new byte[8192];
        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                var res = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct)
                    .ConfigureAwait(false);
                if (res.MessageType == WebSocketMessageType.Close) break;
                if (res.MessageType == WebSocketMessageType.Binary && res.Count > 0)
                {
                    var chunk = new byte[res.Count];
                    Buffer.BlockCopy(buf, 0, chunk, 0, res.Count);
                    pushStream.Write(chunk);
                }
                // Ignore Text frames — reserved for future control messages.
            }
        }
        catch (WebSocketException wse)
        {
            logger.LogInformation("WebSocket ended: {Code} {Msg}", wse.WebSocketErrorCode, wse.Message);
        }
        finally
        {
            try { await transcriber.StopTranscribingAsync().ConfigureAwait(false); } catch { }
            pushStream.Close();
            if (ws.State == WebSocketState.Open)
            {
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false); } catch { }
            }
        }
    }

    private static List<string> ParseCandidateLanguages(string raw)
    {
        var langs = raw?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length is >= 2 and <= 16)
            .Take(4)
            .ToList() ?? [];
        if (langs.Count == 0) langs.Add("en-US");
        return langs;
    }

    private static async Task SendJsonAsync<T>(WebSocket ws, SemaphoreSlim gate, T payload, CancellationToken ct)
    {
        if (ws.State != WebSocketState.Open) return;
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await ws.SendAsync(new ArraySegment<byte>(json), WebSocketMessageType.Text, true, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }
}
