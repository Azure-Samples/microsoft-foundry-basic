using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using chatui.Configuration;
using Microsoft.Extensions.Options;

namespace chatui.Services;

/// <summary>
/// Personal-chat text translator. Bypasses the Azure.AI.Translation.Text SDK
/// because that SDK requests a token audience of
/// <c>https://api.cognitive.microsofttranslator.com/.default</c>, which the
/// App Service IMDS endpoint rejects with HTTP 400. Instead we obtain a
/// standard Cognitive Services bearer
/// (<c>https://cognitiveservices.azure.com/.default</c> — same audience the
/// Speech endpoint uses successfully) and call the Translator v3 REST API
/// directly with the required region header.
/// </summary>
public sealed class TranslatorService
{
    private static readonly string[] TokenScopes = ["https://cognitiveservices.azure.com/.default"];
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly TokenCredential _credential;
    private readonly TranslatorOptions _options;

    public TranslatorService(HttpClient httpClient, TokenCredential credential, IOptions<TranslatorOptions> options)
    {
        _credential = credential;
        _options = options.Value;
        _http = httpClient;
        var endpoint = new Uri(_options.Endpoint.TrimEnd('/') + "/");
        _http.BaseAddress = endpoint.AbsolutePath == "/"
            && endpoint.Host.EndsWith(".cognitiveservices.azure.com", StringComparison.OrdinalIgnoreCase)
            ? new Uri(endpoint, "translator/text/v3.0/")
            : endpoint;
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

        var query = $"translate?api-version=3.0&to={Uri.EscapeDataString(targetLanguage)}";
        if (!string.IsNullOrEmpty(sourceLanguage))
        {
            query += $"&from={Uri.EscapeDataString(sourceLanguage)}";
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, query)
        {
            Content = JsonContent.Create(new[] { new { Text = text } }, options: JsonOpts),
        };
        req.Headers.Add("Ocp-Apim-Subscription-Region", _options.Region);

        if (!string.IsNullOrEmpty(_options.Key))
        {
            // Classic key auth — used because Translator's Entra audience
            // (api.cognitive.microsofttranslator.com) currently 400s from
            // App Service IMDS. Speech works fine with Entra because its
            // audience is cognitiveservices.azure.com.
            req.Headers.Add("Ocp-Apim-Subscription-Key", _options.Key);
        }
        else
        {
            var token = await _credential
                .GetTokenAsync(new TokenRequestContext(TokenScopes), cancellationToken)
                .ConfigureAwait(false);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        }

        using var res = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"Translator API returned {(int)res.StatusCode} {res.ReasonPhrase}: {body}");
        }

        // Translator v3 returns: [ { "detectedLanguage": {"language","score"}, "translations": [ {"text","to"} ] } ]
        using var stream = await res.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var first = doc.RootElement[0];
        var detected = first.TryGetProperty("detectedLanguage", out var d) && d.TryGetProperty("language", out var dl)
            ? dl.GetString()
            : sourceLanguage;
        var t0 = first.GetProperty("translations")[0];

        return new TranslationResult(
            SourceLanguage: detected ?? "unknown",
            TargetLanguage: t0.GetProperty("to").GetString() ?? targetLanguage,
            TranslatedText: t0.GetProperty("text").GetString() ?? string.Empty);
    }

    public sealed record TranslationResult(
        string SourceLanguage,
        string TargetLanguage,
        string TranslatedText);
}
