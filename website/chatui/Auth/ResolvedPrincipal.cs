namespace chatui.Auth;

/// <summary>
/// Normalised view of the person behind the current HTTP request. Populated
/// either from the App Service Easy Auth <c>X-MS-CLIENT-PRINCIPAL</c> header
/// or from the local <see cref="DevAuthOptions"/> override.
/// </summary>
public sealed record ResolvedPrincipal(
    string EntraObjectId,
    string DisplayName,
    string Email,
    string? PreferredLanguage);
