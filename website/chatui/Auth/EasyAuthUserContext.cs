using System.Text;
using System.Text.Json;
using chatui.Data;
using chatui.Data.Entities;
using Microsoft.Extensions.Options;

namespace chatui.Auth;

/// <summary>
/// Resolves the current user from the App Service Easy Auth
/// <c>X-MS-CLIENT-PRINCIPAL</c> header. Falls back to <see cref="DevAuthOptions"/>
/// when running under the Development environment with <c>DevAuth:Enabled=true</c>
/// so that local runs and integration tests work without Easy Auth.
///
/// In dev mode the client must select the impersonated identity per request via
/// <c>X-Dev-Oid</c>, <c>X-Dev-Name</c>, <c>X-Dev-Email</c>, and
/// <c>X-Dev-Lang</c> headers. This is only honoured when <c>DevAuth:Enabled</c>
/// is true and takes precedence over Easy Auth — never in production.
/// </summary>
public sealed class EasyAuthUserContext(
    IHttpContextAccessor httpContextAccessor,
    IUserProfileStore profiles,
    IHostEnvironment environment,
    IOptions<DevAuthOptions> devAuthOptions,
    ILogger<EasyAuthUserContext> logger) : IUserContext
{
    private const string EasyAuthHeaderName = "X-MS-CLIENT-PRINCIPAL";
    private const string DevOidHeader   = "X-Dev-Oid";
    private const string DevNameHeader  = "X-Dev-Name";
    private const string DevEmailHeader = "X-Dev-Email";
    private const string DevLangHeader  = "X-Dev-Lang";

    // Claim types Easy Auth surfaces. The v2 endpoint uses full OIDC URIs.
    private static readonly string[] MicrosoftObjectIdClaimTypes =
    [
        "http://schemas.microsoft.com/identity/claims/objectidentifier",
        "oid",
    ];

    private static readonly string[] SubjectClaimTypes = ["sub"];
    private static readonly string[] IssuerClaimTypes = ["iss"];

    private static readonly string[] EmailClaimTypes =
    [
        "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress",
        "email",
        "preferred_username",
        "upn",
    ];

    private static readonly string[] NameClaimTypes =
    [
        "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name",
        "name",
    ];

    public async Task<User> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var principal = ResolvePrincipal();

        var existing = await profiles.GetOrClaimAsync(principal, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        logger.LogWarning("Rejected an authenticated but uninvited {Provider} user.", principal.Provider);
        throw new UserNotProvisionedException();
    }

    private ResolvedPrincipal ResolvePrincipal()
    {
        var http = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available on the current request.");

        if (environment.IsDevelopment() && devAuthOptions.Value.Enabled)
        {
            // Optional per-request override for multi-user testing on the same
            // tenant. Only trusted inside the Development environment.
            // WebSocket handshakes can't carry custom headers from a browser,
            // so also accept the same values from the query string.
            var oid = http.Request.Headers.TryGetValue(DevOidHeader, out var oidHeader) && !string.IsNullOrWhiteSpace(oidHeader)
                ? oidHeader.ToString()
                : http.Request.Query[DevOidHeader].ToString();
            if (!string.IsNullOrWhiteSpace(oid))
            {
                string HeaderOrQuery(string key) => http.Request.Headers.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)
                    ? v.ToString()
                    : http.Request.Query[key].ToString();
                var name = string.IsNullOrWhiteSpace(HeaderOrQuery(DevNameHeader)) ? oid : HeaderOrQuery(DevNameHeader);
                var email = HeaderOrQuery(DevEmailHeader);
                var lang = string.IsNullOrWhiteSpace(HeaderOrQuery(DevLangHeader)) ? "en" : HeaderOrQuery(DevLangHeader);
                return new ResolvedPrincipal(
                    IdentityProviders.Microsoft,
                    oid,
                    "development",
                    name,
                    email,
                    lang);
            }

            throw new DevPersonaRequiredException();
        }

        if (TryReadEasyAuthPrincipal(http, out var principal))
        {
            return principal;
        }

        throw new UnauthorizedAccessException(
            "Missing X-MS-CLIENT-PRINCIPAL header; App Service Easy Auth must be enabled.");
    }

    internal static bool TryReadEasyAuthPrincipal(HttpContext http, out ResolvedPrincipal principal)
    {
        principal = default!;

        if (!http.Request.Headers.TryGetValue(EasyAuthHeaderName, out var headerValues))
        {
            return false;
        }

        var encoded = headerValues.ToString();
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return false;
        }

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(decoded);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;

            if (!root.TryGetProperty("claims", out var claimsElement)
                || claimsElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var authenticationType = root.TryGetProperty("auth_typ", out var authTypeElement)
                ? authTypeElement.GetString()
                : null;
            var provider = IdentityProviders.Normalize(authenticationType);
            if (provider is null)
            {
                return false;
            }

            string? objectId = null;
            string? subject = null;
            string? issuer = null;
            string? email = null;
            string? name = null;

            foreach (var claim in claimsElement.EnumerateArray())
            {
                if (!claim.TryGetProperty("typ", out var typElement)
                    || !claim.TryGetProperty("val", out var valElement))
                {
                    continue;
                }

                var typ = typElement.GetString();
                var val = valElement.GetString();
                if (string.IsNullOrEmpty(typ) || string.IsNullOrEmpty(val))
                {
                    continue;
                }

                if (objectId is null && MicrosoftObjectIdClaimTypes.Contains(typ, StringComparer.OrdinalIgnoreCase))
                {
                    objectId = val;
                }
                else if (subject is null && SubjectClaimTypes.Contains(typ, StringComparer.OrdinalIgnoreCase))
                {
                    subject = val;
                }
                else if (issuer is null && IssuerClaimTypes.Contains(typ, StringComparer.OrdinalIgnoreCase))
                {
                    issuer = val;
                }
                else if (email is null && EmailClaimTypes.Contains(typ, StringComparer.OrdinalIgnoreCase))
                {
                    email = val;
                }
                else if (name is null && NameClaimTypes.Contains(typ, StringComparer.OrdinalIgnoreCase))
                {
                    name = val;
                }
            }

            var stableSubject = provider == IdentityProviders.Microsoft ? objectId ?? subject : subject;
            if (string.IsNullOrWhiteSpace(stableSubject))
            {
                return false;
            }

            principal = new ResolvedPrincipal(
                Provider: provider,
                Subject: stableSubject,
                Issuer: issuer ?? provider,
                DisplayName: name ?? email ?? "Invited user",
                Email: email ?? string.Empty,
                PreferredLanguage: null);
            return true;
        }
    }
}

public sealed class DevPersonaRequiredException : UnauthorizedAccessException;

public sealed class UserNotProvisionedException : UnauthorizedAccessException;
