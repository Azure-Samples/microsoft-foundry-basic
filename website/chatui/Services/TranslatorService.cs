using Azure.AI.Translation.Text;
using Azure.Core;
using chatui.Configuration;
using Microsoft.Extensions.Options;

namespace chatui.Services;

/// <summary>
/// Thin wrapper around <see cref="TextTranslationClient"/> that hides the
/// SDK's tuple-shaped response and always uses Entra credentials (no keys).
/// Used only on the personal-chat path; meetings translate on the client via
/// the Speech Translation SDK.
/// </summary>
public sealed class TranslatorService
{
    private readonly TextTranslationClient _client;

    public TranslatorService(TokenCredential credential, IOptions<TranslatorOptions> options)
    {
        var opts = options.Value;
        _client = new TextTranslationClient(
            credential,
            new Uri(opts.Endpoint),
            opts.Region,
            new TextTranslationClientOptions());
    }

    public async Task<TranslationResult> TranslateAsync(
        string text,
        string targetLanguage,
        string? sourceLanguage = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text must not be empty.", nameof(text));
        }

        var response = await _client.TranslateAsync(
            targetLanguages: [targetLanguage],
            content: [text],
            sourceLanguage: sourceLanguage,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var translated = response.Value[0];
        var detected = translated.DetectedLanguage?.Language ?? sourceLanguage ?? "unknown";
        var text0 = translated.Translations[0];

        return new TranslationResult(
            SourceLanguage: detected,
            TargetLanguage: text0.TargetLanguage ?? targetLanguage,
            TranslatedText: text0.Text);
    }

    public sealed record TranslationResult(
        string SourceLanguage,
        string TargetLanguage,
        string TranslatedText);
}
