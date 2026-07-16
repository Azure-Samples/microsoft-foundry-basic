using System.ComponentModel.DataAnnotations;

namespace chatui.Configuration;

public class JoinTokenOptions
{
    public const string SectionName = "JoinToken";

    /// <summary>Issuer claim (iss) in the JWT. Defaults to the app's base URI at issue time.</summary>
    public string? Issuer { get; init; }

    [MinLength(1)]
    public string Audience { get; init; } = "meeting-join";

    [Range(1, 168)]
    public int LifetimeHours { get; init; } = 24;
}
