using System.ComponentModel.DataAnnotations;

namespace chatui.Configuration;

public class TranslatorOptions
{
    public const string SectionName = "Translator";

    [Required]
    [Url]
    public string Endpoint { get; init; } = default!;

    /// <summary>
    /// Azure region of the Translator resource. Required by Translator's
    /// global endpoint to route the request to the correct backend.
    /// </summary>
    [Required]
    public string Region { get; init; } = default!;

    /// <summary>
    /// Optional subscription key. If set, the service uses the classic
    /// <c>Ocp-Apim-Subscription-Key</c> header. If null/empty, the service
    /// falls back to Entra managed identity (which currently 400s from App
    /// Service IMDS for Translator's specific audience — see
    /// <see cref="chatui.Services.TranslatorService"/>).
    /// </summary>
    public string? Key { get; init; }
}
