using System.Diagnostics;
using Azure.Core;
using chatui.Auth;
using chatui.Configuration;
using chatui.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.CognitiveServices.Speech;

namespace chatui.Controllers;

/// <summary>
/// Speech token minting + server-side Neural TTS proxy. The TTS endpoint
/// synthesises text to MP3 server-side using the Azure Speech REST API, so
/// clients get a real Neural voice through a plain <c>&lt;audio&gt;</c>
/// element — no client SDK, no per-utterance AudioContext quirks on mobile.
/// </summary>
[ApiController]
[Route("api/speech")]
public class SpeechController(
    IUserContext userContext,
    TokenCredential credential,
    IOptions<SpeechOptions> speechOptions,
    TranslatorTelemetry telemetry,
    ILogger<SpeechController> logger) : ControllerBase
{
    private static readonly string[] CognitiveServicesScopes =
        ["https://cognitiveservices.azure.com/.default"];

    [HttpGet("token")]
    public async Task<ActionResult<SpeechTokenResponse>> GetToken(CancellationToken ct)
    {
        // Force the sign-in gate + user provisioning; the client only calls this
        // when it's about to talk to the Speech SDK from a signed-in session.
        _ = await userContext.GetCurrentAsync(ct).ConfigureAwait(false);

        return StatusCode(StatusCodes.Status410Gone);
    }

    /// <summary>
    /// Synthesise <paramref name="request"/>.Text into an MP3 stream using the
    /// requested Neural voice. Returns audio/mpeg the caller can drop straight
    /// into an <c>&lt;audio&gt;</c> element.
    /// </summary>
    [HttpPost("tts")]
    public async Task<IActionResult> Tts([FromBody] TtsRequest request, CancellationToken ct)
    {
        var user = await userContext.GetCurrentAsync(ct).ConfigureAwait(false);

        if (request is null || string.IsNullOrWhiteSpace(request.Text))
            return BadRequest("text is required");
        if (string.IsNullOrWhiteSpace(request.Voice))
            return BadRequest("voice is required");

        using var activity = telemetry.StartActivity("tts.synth", ActivityKind.Server);
        var sw = Stopwatch.StartNew();

        // Truncate to keep synth latency + billable characters bounded.
        var text = request.Text.Length > 800 ? request.Text[..800] : request.Text;
        var voice = request.Voice;
        var lang = voice[..voice.LastIndexOf('-', voice.IndexOf('N') - 1 < 0 ? voice.Length - 1 : voice.IndexOf('N') - 1)];
        // Fall back to a simple bcp47 extraction (voice names look like `ru-RU-SvetlanaNeural`).
        var dashParts = voice.Split('-');
        if (dashParts.Length >= 2) lang = $"{dashParts[0]}-{dashParts[1]}";

        activity?.SetTag("aitr.voice", voice);
        activity?.SetTag("aitr.language", lang);
        activity?.SetTag("aitr.user_id", user.Id.ToString());
        activity?.SetTag("aitr.input_chars", text.Length);

        var accessToken = await credential
            .GetTokenAsync(new TokenRequestContext(CognitiveServicesScopes), ct)
            .ConfigureAwait(false);

        var opts = speechOptions.Value;

        var ssml = $"""
            <speak version='1.0' xml:lang='{Escape(lang)}'>
              <voice name='{Escape(voice)}'>{Escape(text)}</voice>
            </speak>
            """;

        var config = SpeechConfig.FromEndpoint(new UriBuilder(opts.Endpoint) { Scheme = "wss", Port = -1 }.Uri);
        config.AuthorizationToken = $"aad#{opts.ResourceId}#{accessToken.Token}";
        config.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Audio24Khz48KBitRateMonoMp3);
        using var synthesizer = new SpeechSynthesizer(config, null);
        using var result = await synthesizer.SpeakSsmlAsync(ssml).WaitAsync(ct).ConfigureAwait(false);
        if (result.Reason != ResultReason.SynthesizingAudioCompleted)
        {
            logger.LogWarning("Private Speech synthesis failed ({Reason})", result.Reason);
            return StatusCode(StatusCodes.Status502BadGateway, "Speech synthesis unavailable.");
        }

        var bytes = result.AudioData;
        sw.Stop();

        var tags = new TagList
        {
            { "voice",    voice },
            { "language", lang  },
        };
        telemetry.TtsCalls.Add(1, tags);
        telemetry.TtsChars.Add(text.Length, tags);
        telemetry.TtsDuration.Record(sw.Elapsed.TotalMilliseconds, tags);
        activity?.SetTag("aitr.audio_bytes", bytes.Length);

        return File(bytes, "audio/mpeg");
    }

    private static string Escape(string s) => System.Security.SecurityElement.Escape(s) ?? s;

    public sealed record SpeechTokenResponse(string Token, string Region, DateTimeOffset ExpiresAt);
    public sealed record TtsRequest(string Text, string Voice);
}
