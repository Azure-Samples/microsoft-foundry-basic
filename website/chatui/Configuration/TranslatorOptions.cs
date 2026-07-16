using System.ComponentModel.DataAnnotations;

namespace chatui.Configuration;

public class TranslatorOptions
{
    public const string SectionName = "Translator";

    [Required]
    [Url]
    public string Endpoint { get; init; } = default!;

    /// <summary>
    /// Azure region of the Translator resource. Required when authenticating with
    /// an Entra token (in addition to the endpoint) — the Translator service uses
    /// it to route the request to the correct backend.
    /// </summary>
    [Required]
    public string Region { get; init; } = default!;
}
