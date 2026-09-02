using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using Azure.Core;
using chatui.Auth;
using chatui.Configuration;
using chatui.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

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
    IHttpClientFactory httpFactory,
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

        var accessToken = await credential
            .GetTokenAsync(new TokenRequestContext(CognitiveServicesScopes), ct)
            .ConfigureAwait(false);

        var opts = speechOptions.Value;
        var wrapped = $"aad#{opts.ResourceId}#{accessToken.Token}";

        return Ok(new SpeechTokenResponse(wrapped, opts.Region, accessToken.ExpiresOn));
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

        // Exchange the AAD access token for a short-lived Speech STS token.
        // The STS endpoint must be the account's custom subdomain (not the
        // regional endpoint) for AAD authentication to work.
        var http = httpFactory.CreateClient();
        var accountBase = opts.Endpoint.TrimEnd('/');
        using var issueReq = new HttpRequestMessage(
            HttpMethod.Post,
            $"{accountBase}/sts/v1.0/issueToken");
        issueReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Token);
        using var issueResp = await http.SendAsync(issueReq, ct).ConfigureAwait(false);
        if (!issueResp.IsSuccessStatusCode)
        {
            var err = await issueResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            logger.LogWarning("Speech STS issueToken failed {Status}: {Body}", issueResp.StatusCode, err);
            return StatusCode((int)issueResp.StatusCode, "issueToken: " + err);
        }
        var speechJwt = (await issueResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).Trim();

        var ssml = $"""
            <speak version='1.0' xml:lang='{Escape(lang)}'>
              <voice name='{Escape(voice)}'>{Escape(text)}</voice>
            </speak>
            """;

        var url = $"https://{opts.Region}.tts.speech.microsoft.com/cognitiveservices/v1";
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(ssml, Encoding.UTF8, "application/ssml+xml"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", speechJwt);
        req.Headers.Add("X-Microsoft-OutputFormat", "audio-24khz-48kbitrate-mono-mp3");
        req.Headers.Add("User-Agent", "ai-translator");

        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            logger.LogWarning("TTS synth failed {Status}: {Body}", resp.StatusCode, err);
            return StatusCode((int)resp.StatusCode, err);
        }

        var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
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
