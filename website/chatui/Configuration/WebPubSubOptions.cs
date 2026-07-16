using System.ComponentModel.DataAnnotations;

namespace chatui.Configuration;

public class WebPubSubOptions
{
    public const string SectionName = "WebPubSub";

    [Required]
    [Url]
    public string Endpoint { get; init; } = default!;

    [Required]
    public string Hub { get; init; } = "meetings";

    /// <summary>How long the client access token remains valid. Client re-joins to refresh.</summary>
    [Range(5, 240)]
    public int ClientTokenLifetimeMinutes { get; init; } = 60;
}
