using System.Diagnostics;
using System.Text.RegularExpressions;
using chatui.Auth;
using chatui.Services;
using Microsoft.AspNetCore.Mvc;

namespace chatui.Controllers;

/// <summary>
/// Small wrapper around <see cref="TranslatorService"/> that lets the browser
/// translate arbitrary snippets (e.g. Talk-mode utterances) without exposing
/// the Translator subscription key or an Entra token to the client.
///
/// When <c>soften=true</c> is set, the translated output is post-processed
/// through the Foundry agent's <c>civilize</c> task, which rewrites profanity
/// into mild, meaning-preserving equivalents in the target language. This
/// prevents Speech STT's asterisk masking + Neural TTS from reading out
/// "star star star" for every censored word. To keep latency down, civilize
/// is only invoked when a cheap local heuristic actually detects profanity
/// markers in the source or translated text — clean utterances skip it.
/// </summary>
[ApiController]
[Route("api/translate")]
public class TranslateController(
    IUserContext userContext,
    TranslatorService translator,
    FoundryAgentService foundry,
    TranslatorTelemetry telemetry,
    ILogger<TranslateController> logger) : ControllerBase
{
    // Very rough profanity gate. Matches:
    //  * any word containing asterisks (masked by STT/Translator), and
    //  * common English + Russian obscenity roots (case-insensitive, with word
    //    boundaries in Unicode-aware mode so Cyrillic is handled).
    // False positives are cheap (we just spend one extra GPT call) — false
    // negatives are the real cost, so err on the side of matching more.
    private static readonly Regex ProfanityRegex = new(
        @"\*{2,}|\b(?:f+u+c+k+\w*|sh+i+t+\w*|b+i+t+c+h+\w*|a+s+s+h+o+l+e+\w*|c+u+n+t+\w*|d+i+c+k+\w*|bastard\w*|damn\w*|hell\b|crap\w*|piss\w*)\b|(?:\p{L}*(?:хуй|пизд|ебан|еба[лть]|ебис|ёбан|ёбист|бля[тд]|блядь|блядс|мудак|мудил|сука|сучк|нахер|нахуй|похуй|нихуя|говн|дерьм|срать|срет|срал|мраз|дура[кч]|идиот|тварь|подонок)\p{L}*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static bool ContainsProfanity(string? s)
        => !string.IsNullOrEmpty(s) && ProfanityRegex.IsMatch(s);

    [HttpPost]
    public async Task<ActionResult<TranslateResponse>> Translate([FromBody] TranslateRequest request, CancellationToken ct)
    {
        var user = await userContext.GetCurrentAsync(ct).ConfigureAwait(false);

        if (request is null || string.IsNullOrWhiteSpace(request.Text))
            return BadRequest("text is required");
        if (string.IsNullOrWhiteSpace(request.To))
            return BadRequest("to is required");

        // Truncate to keep round-trip time predictable.
        var text = request.Text.Length > 2000 ? request.Text[..2000] : request.Text;

        using var activity = telemetry.StartActivity("translate", ActivityKind.Server);
        activity?.SetTag("aitr.to", request.To);
        if (!string.IsNullOrWhiteSpace(request.From)) activity?.SetTag("aitr.from", request.From);
        activity?.SetTag("aitr.soften.requested", request.Soften == true);

        var translateSw = Stopwatch.StartNew();
        var result = await translator
            .TranslateAsync(text, request.To, string.IsNullOrWhiteSpace(request.From) ? null : request.From, ct)
            .ConfigureAwait(false);
        translateSw.Stop();

        var translated = result.TranslatedText;
        var softened = false;
        if (request.Soften == true
            && !string.IsNullOrWhiteSpace(translated)
            && (ContainsProfanity(text) || ContainsProfanity(translated)))
        {
            try
            {
                var s = await foundry.CivilizeAsync(translated, result.TargetLanguage, ct)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(s))
                {
                    translated = s;
                    softened = true;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Civilize step failed; returning raw translation.");
            }
        }

        // Metrics — dimensions kept low-cardinality (no per-user tag, since
        // that blows up cardinality on the metrics backend).
        var langTags = new TagList
        {
            { "detected", result.SourceLanguage },
            { "target",   result.TargetLanguage },
            { "softened", softened },
        };
        telemetry.TranslateCalls.Add(1, langTags);
        telemetry.TranslateInputChars.Add(text.Length, langTags);
        telemetry.TranslateOutputChars.Add(translated?.Length ?? 0, langTags);
        telemetry.TranslateDuration.Record(translateSw.Elapsed.TotalMilliseconds, langTags);

        // High-cardinality per-user detail belongs on the span, not the metric.
        activity?.SetTag("aitr.detected", result.SourceLanguage);
        activity?.SetTag("aitr.target", result.TargetLanguage);
        activity?.SetTag("aitr.softened", softened);
        activity?.SetTag("aitr.user_id", user.Id.ToString());
        activity?.SetTag("aitr.input_chars", text.Length);
        activity?.SetTag("aitr.output_chars", translated?.Length ?? 0);

        return Ok(new TranslateResponse(
            DetectedSourceLanguage: result.SourceLanguage,
            TargetLanguage: result.TargetLanguage,
            TranslatedText: translated ?? string.Empty,
            Softened: softened));
    }

    public sealed record TranslateRequest(string Text, string To, string? From = null, bool? Soften = null);

    public sealed record TranslateResponse(
        string DetectedSourceLanguage,
        string TargetLanguage,
        string TranslatedText,
        bool Softened);
}
