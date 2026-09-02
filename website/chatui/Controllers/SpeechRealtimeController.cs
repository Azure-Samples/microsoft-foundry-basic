using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Azure.Core;
using chatui.Auth;
using chatui.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace chatui.Controllers;

/// <summary>
/// Bridges the browser to the Azure OpenAI Realtime API for the
/// <c>gpt-live-transcribe</c> and <c>gpt-4o-transcribe-diarize</c> models. The
/// browser sends 16-kHz mono int16 PCM binary frames over WebSocket; we
/// re-encode them as base64 <c>input_audio_buffer.append</c> JSON events
/// upstream, then forward transcript deltas + completions back to the browser
/// as the same simple <c>{ type, text, language, speakerId }</c> envelope
/// used by /api/speech/transcribe (SpeechStreamController).
///
/// This means the browser can flip between "diarized ConversationTranscriber"
/// and "GPT high-accuracy transcription" simply by opening a different URL —
/// no other client-side changes needed.
///
/// Modes:
///  * <c>?mode=gpt</c> (default here) → <c>gpt-live-transcribe</c>. Best real-world
///    accuracy: robust to noise, quiet speech, business terms, mixed language,
///    codes/dates/addresses. NO built-in diarization.
///  * <c>?mode=diarize</c> → <c>gpt-4o-transcribe-diarize</c>. Similar quality
///    with built-in speaker separation (Speaker 1, Speaker 2, …).
/// </summary>
[ApiController]
[Route("api/speech")]
public class SpeechRealtimeController(
    IUserContext userContext,
    TokenCredential credential,
    IOptions<ChatApiOptions> chatOpts,
    ILogger<SpeechRealtimeController> logger) : ControllerBase
{
    // Per Foundry Realtime docs, WebSocket handshakes to /openai/v1/realtime*
    // authenticate against ai.azure.com (not the generic cognitiveservices
    // audience). Using the wrong scope returns 400 during upgrade.
    private static readonly string[] AoaiScopes =
        ["https://ai.azure.com/.default"];

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [HttpGet("transcribe-gpt")]
    public async Task Transcribe(
        [FromQuery] string langs = "en",
        [FromQuery] string mode = "gpt",
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

        var opts = chatOpts.Value;
        // AIProjectEndpoint is scoped to the project; realtime lives on the
        // account subdomain. Strip the /api/projects/... tail.
        var accountBase = new Uri(opts.AIProjectEndpoint).GetLeftPart(UriPartial.Authority);

        // Pick the deployment based on requested mode.
        var deployment = mode?.ToLowerInvariant() switch
        {
            "diarize" => "gpt-4o-transcribe-diarize",
            _         => "gpt-live-transcribe",
        };

        // Two-letter language hint improves accuracy + latency per docs.
        var primaryLang = ParsePrimaryLanguage(langs);

        var accessToken = await credential
            .GetTokenAsync(new TokenRequestContext(AoaiScopes), ct)
            .ConfigureAwait(false);

        // Realtime WS URL. Docs specify no api-version param — the /openai/v1
        // GA endpoint has canonical routing.
        var wsUrl = $"{accountBase.Replace("https://", "wss://")}/openai/v1/realtime?intent=transcription";

        WebSocket upstream;
        try
        {
            upstream = await SpeechS2SController.ManualWebSocketConnectAsync(wsUrl, accessToken.Token, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to connect to Azure OpenAI Realtime endpoint {Url}", wsUrl);
            await SendJsonAsync(clientWs, null, new { type = "error", message = "Cannot reach Realtime endpoint: " + ex.Message }, ct)
                .ConfigureAwait(false);
            try { await clientWs.CloseAsync(WebSocketCloseStatus.InternalServerError, "upstream failure", ct).ConfigureAwait(false); } catch { }
            return;
        }
        using var _upstreamDispose = upstream;

        // Configure the transcription session. Per the Realtime API reference
        // (openai/realtime.md) and Foundry docs:
        //   * gpt-live-transcribe / gpt-transcribe do NOT support
        //     turn_detection — send `null`, commit audio buffer manually every
        //     ~2 seconds so completed transcripts flow back.
        //   * gpt-4o-transcribe-diarize supports server_vad and needs it to
        //     produce speaker-labelled chunks.
        //   * `delay` is only accepted by gpt-realtime-whisper.
        var usesManualCommit = deployment is "gpt-live-transcribe" or "gpt-transcribe";
        object? turnDetection = usesManualCommit
            ? null
            : new
            {
                type = "server_vad",
                // Higher threshold + longer silence tolerance so we don't cut
                // people off mid-thought. Users complained that the previous
                // settings shredded speech into single-word snippets.
                threshold = 0.55,
                prefix_padding_ms = 400,
                silence_duration_ms = 1500,
            };

        var sessionConfig = new
        {
            type = "session.update",
            session = new
            {
                type = "transcription",
                audio = new
                {
                    input = new
                    {
                        format = new
                        {
                            type = "audio/pcm",
                            rate = 24000,
                        },
                        transcription = new
                        {
                            model = deployment,
                            language = primaryLang,
                        },
                        turn_detection = turnDetection,
                    },
                },
            },
        };
        await SendUpstreamJsonAsync(upstream, sessionConfig, ct).ConfigureAwait(false);

        var clientSendLock = new SemaphoreSlim(1, 1);
        var upstreamSendLock = new SemaphoreSlim(1, 1);

        // Pump audio: browser (binary PCM) → upstream (base64 JSON append).
        //
        // For models without server VAD (gpt-live-transcribe / gpt-transcribe)
        // we implement a small silence-aware commit heuristic here so the
        // transcription pipeline emits `.completed` events at natural pauses
        // rather than mid-word:
        //
        //   * Measure RMS energy of each incoming PCM buffer (int16 mono).
        //   * Track consecutive silence duration in milliseconds.
        //   * Commit only when we have ≥ 1.5 s of audio AND ≥ 700 ms of
        //     silence — i.e. the speaker just paused.
        //   * Hard-flush at 15 s so a rambling monologue eventually shows up.
        //
        // This gives long-form thoughts a chance to complete before we ask
        // the model for a transcript.
        var audioPump = Task.Run(async () =>
        {
            var buf = new byte[8192];
            const int sampleRate = 24000;
            const int bytesPerSample = 2;
            const int bytesPerSec = sampleRate * bytesPerSample;
            const int minBufferBytes = (int)(bytesPerSec * 1.5);   // require ≥1.5s of audio before commit
            const int silenceCommitMs = 700;                       // need ≥700ms of silence
            const int hardFlushBytes = bytesPerSec * 15;           // hard cap 15s
            const short silenceThreshold = 500;                    // int16 RMS threshold (~1.5% amplitude)

            var bytesInBuffer = 0;
            var silenceMsAccum = 0;

            try
            {
                while (!ct.IsCancellationRequested && clientWs.State == WebSocketState.Open)
                {
                    var res = await clientWs.ReceiveAsync(new ArraySegment<byte>(buf), ct).ConfigureAwait(false);
                    if (res.MessageType == WebSocketMessageType.Close) break;
                    if (res.MessageType != WebSocketMessageType.Binary || res.Count == 0) continue;

                    var b64 = Convert.ToBase64String(buf, 0, res.Count);
                    await SendUpstreamJsonAsync(upstream, new
                    {
                        type = "input_audio_buffer.append",
                        audio = b64,
                    }, ct).ConfigureAwait(false);

                    if (!usesManualCommit) continue;

                    bytesInBuffer += res.Count;
                    var chunkDurationMs = (res.Count / (double)bytesPerSample) / sampleRate * 1000.0;

                    // Cheap RMS on int16 mono PCM to detect silence.
                    long sumSquares = 0;
                    var sampleCount = res.Count / bytesPerSample;
                    for (var i = 0; i < sampleCount; i++)
                    {
                        var s = (short)(buf[i * 2] | (buf[i * 2 + 1] << 8));
                        sumSquares += s * s;
                    }
                    var rms = sampleCount > 0 ? Math.Sqrt(sumSquares / (double)sampleCount) : 0;
                    var isSilent = rms < silenceThreshold;

                    silenceMsAccum = isSilent ? silenceMsAccum + (int)chunkDurationMs : 0;

                    var shouldCommit =
                        (bytesInBuffer >= minBufferBytes && silenceMsAccum >= silenceCommitMs)
                        || bytesInBuffer >= hardFlushBytes;

                    if (shouldCommit)
                    {
                        await SendUpstreamJsonAsync(upstream, new
                        {
                            type = "input_audio_buffer.commit",
                        }, ct).ConfigureAwait(false);
                        bytesInBuffer = 0;
                        silenceMsAccum = 0;
                    }
                }
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
            {
                // benign — cleanup below
            }
        }, ct);

        // Pump events: upstream (JSON events) → browser (simplified JSON).
        // Language and speakerId are derived per-event where possible.
        var eventPump = Task.Run(async () =>
        {
            var buf = new byte[16384];
            var acc = new MemoryStream();
            string? lastPartial = null;
            try
            {
                while (!ct.IsCancellationRequested && upstream.State == WebSocketState.Open)
                {
                    acc.SetLength(0);
                    WebSocketReceiveResult res;
                    do
                    {
                        res = await upstream.ReceiveAsync(new ArraySegment<byte>(buf), ct).ConfigureAwait(false);
                        if (res.MessageType == WebSocketMessageType.Close) return;
                        acc.Write(buf, 0, res.Count);
                    } while (!res.EndOfMessage);
                    if (res.MessageType != WebSocketMessageType.Text) continue;

                    var payload = Encoding.UTF8.GetString(acc.GetBuffer(), 0, (int)acc.Length);
                    using var doc = JsonDocument.Parse(payload);
                    var root = doc.RootElement;
                    var evtType = root.TryGetProperty("type", out var t) ? t.GetString() : null;
                    if (evtType is null) continue;

                    switch (evtType)
                    {
                        case "conversation.item.input_audio_transcription.delta":
                        case "conversation.item.audio_transcription.delta":
                        {
                            var delta = root.TryGetProperty("delta", out var d) ? d.GetString() : null;
                            if (!string.IsNullOrEmpty(delta))
                            {
                                lastPartial = (lastPartial ?? "") + delta;
                                await SendJsonAsync(clientWs, clientSendLock, new
                                {
                                    type = "transcribing",
                                    speakerId = ReadSpeaker(root),
                                    text = lastPartial,
                                }, ct).ConfigureAwait(false);
                            }
                            break;
                        }
                        case "conversation.item.input_audio_transcription.completed":
                        case "conversation.item.audio_transcription.completed":
                        {
                            var text = root.TryGetProperty("transcript", out var tr) ? tr.GetString() : null;
                            if (string.IsNullOrWhiteSpace(text))
                            {
                                lastPartial = null;
                                break;
                            }
                            var lang = root.TryGetProperty("language", out var l) ? l.GetString() : null;
                            await SendJsonAsync(clientWs, clientSendLock, new
                            {
                                type = "transcribed",
                                speakerId = ReadSpeaker(root),
                                text,
                                language = lang ?? primaryLang,
                            }, ct).ConfigureAwait(false);
                            lastPartial = null;
                            break;
                        }
                        case "input_audio_buffer.speech_started":
                        case "input_audio_buffer.speech_stopped":
                            // no-op — VAD signals we don't currently surface
                            break;
                        case "error":
                        {
                            var errMsg = root.TryGetProperty("error", out var er) && er.TryGetProperty("message", out var em)
                                ? em.GetString() ?? "unknown"
                                : "unknown";
                            logger.LogWarning("Realtime upstream error: {Msg}", errMsg);
                            await SendJsonAsync(clientWs, clientSendLock, new
                            {
                                type = "error",
                                message = errMsg,
                            }, ct).ConfigureAwait(false);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
            {
                // benign
            }
        }, ct);

        // Race the two pumps: exit as soon as either side is done.
        await Task.WhenAny(audioPump, eventPump).ConfigureAwait(false);

        try { if (upstream.State == WebSocketState.Open) await upstream.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false); } catch { }
        try { if (clientWs.State == WebSocketState.Open) await clientWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false); } catch { }
    }

    private static string ParsePrimaryLanguage(string raw)
    {
        // Accept "en-US,ru-RU,de-DE" — pluck the 2-letter head of the first
        // entry. Foundry docs recommend an ISO-639-1 hint.
        var first = raw?.Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "en";
        return first.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant() ?? "en";
    }

    private static string? ReadSpeaker(JsonElement root)
    {
        // Diarize model emits speaker labels on the item metadata.
        if (root.TryGetProperty("speaker", out var sp) && sp.ValueKind == JsonValueKind.String)
            return sp.GetString();
        if (root.TryGetProperty("item", out var item) && item.TryGetProperty("speaker", out var sp2))
            return sp2.GetString();
        return null;
    }

    private static async Task SendJsonAsync<T>(WebSocket ws, SemaphoreSlim? gate, T payload, CancellationToken ct)
    {
        if (ws.State != WebSocketState.Open) return;
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOpts);
        if (gate is not null) await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await ws.SendAsync(new ArraySegment<byte>(json), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }
        finally
        {
            gate?.Release();
        }
    }

    private static async Task SendUpstreamJsonAsync<T>(WebSocket ws, T payload, CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOpts);
        await ws.SendAsync(new ArraySegment<byte>(json), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
    }
}
