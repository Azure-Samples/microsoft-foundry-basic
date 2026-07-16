using System.ComponentModel.DataAnnotations;

namespace chatui.Configuration;

public class SpeechOptions
{
    public const string SectionName = "Speech";

    [Required]
    [Url]
    public string Endpoint { get; init; } = default!;

    [Required]
    public string Region { get; init; } = default!;

    /// <summary>
    /// Full Azure resource ID of the Speech account, e.g.
    /// /subscriptions/…/resourceGroups/…/providers/Microsoft.CognitiveServices/accounts/…
    /// Needed to build the `aad#{resourceId}#{bearer}` authorization token
    /// consumed by the Speech SDK.
    /// </summary>
    [Required]
    public string ResourceId { get; init; } = default!;
}
