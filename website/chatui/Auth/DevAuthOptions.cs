using System.ComponentModel.DataAnnotations;

namespace chatui.Auth;

/// <summary>
/// Local-development shortcut for impersonating an Entra user without going
/// through App Service Easy Auth. Only honoured when the ASP.NET Core
/// environment is <c>Development</c> and <see cref="Enabled"/> is <c>true</c>.
/// Never set these values in stg/prod appsettings.
/// </summary>
public sealed class DevAuthOptions
{
    public const string SectionName = "DevAuth";

    public bool Enabled { get; init; }

    [MinLength(1)]
    public string EntraObjectId { get; init; } = string.Empty;

    [MinLength(1)]
    public string DisplayName { get; init; } = string.Empty;

    [EmailAddress]
    public string Email { get; init; } = string.Empty;

    public string PreferredLanguage { get; init; } = "en";
}
