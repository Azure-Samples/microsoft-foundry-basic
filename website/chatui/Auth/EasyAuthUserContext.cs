using System.Text;
using System.Text.Json;
using chatui.Data;
using chatui.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace chatui.Auth;

/// <summary>
/// Resolves the current user from the App Service Easy Auth
/// <c>X-MS-CLIENT-PRINCIPAL</c> header. Falls back to <see cref="DevAuthOptions"/>
/// when running under the Development environment with <c>DevAuth:Enabled=true</c>
/// so that local runs and integration tests work without Easy Auth.
///
/// In dev mode the client may additionally override the impersonated identity
/// per request via <c>X-Dev-Oid</c>, <c>X-Dev-Name</c>, <c>X-Dev-Email</c>,
/// <c>X-Dev-Lang</c> headers. This is only honoured when Easy Auth is not
/// wired and <c>DevAuth:Enabled</c> is true — never in production.
/// </summary>
public sealed class EasyAuthUserContext(
    IHttpContextAccessor httpContextAccessor,
    TranslatorDbContext dbContext,
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
    private static readonly string[] ObjectIdClaimTypes =
    [
        "http://schemas.microsoft.com/identity/claims/objectidentifier",
        "oid",
    ];

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

    public async Task<User> GetOrCreateCurrentAsync(CancellationToken cancellationToken = default)
    {
        var principal = ResolvePrincipal();

        var existing = await dbContext.Users
            .FirstOrDefaultAsync(u => u.EntraObjectId == principal.EntraObjectId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        var user = new User
        {
            EntraObjectId = principal.EntraObjectId,
            DisplayName = principal.DisplayName,
            Email = principal.Email,
            PreferredLanguage = string.IsNullOrWhiteSpace(principal.PreferredLanguage)
                ? "en"
                : principal.PreferredLanguage!,
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Created user row for Entra oid {EntraObjectId} ({DisplayName})",
            principal.EntraObjectId,
            principal.DisplayName);

        return user;
    }

    private ResolvedPrincipal ResolvePrincipal()
    {
        var http = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No HttpContext available on the current request.");

        if (TryReadEasyAuthPrincipal(http, out var principal))
        {
            return principal;
        }

        if (environment.IsDevelopment() && devAuthOptions.Value is { Enabled: true } dev)
        {
            // Optional per-request override for multi-user testing on the same
            // tenant. Only trusted inside the Development environment.
            if (http.Request.Headers.TryGetValue(DevOidHeader, out var oid) && !string.IsNullOrWhiteSpace(oid))
            {
                var name = http.Request.Headers.TryGetValue(DevNameHeader, out var n) ? n.ToString() : oid.ToString();
                var email = http.Request.Headers.TryGetValue(DevEmailHeader, out var e) ? e.ToString() : string.Empty;
                var lang = http.Request.Headers.TryGetValue(DevLangHeader, out var l) ? l.ToString() : "en";
                return new ResolvedPrincipal(oid.ToString(), name, email, lang);
            }

            return new ResolvedPrincipal(
                dev.EntraObjectId,
                dev.DisplayName,
                dev.Email,
                dev.PreferredLanguage);
        }

        throw new UnauthorizedAccessException(
            "Missing X-MS-CLIENT-PRINCIPAL header; App Service Easy Auth must be enabled.");
    }

    private static bool TryReadEasyAuthPrincipal(HttpContext http, out ResolvedPrincipal principal)
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

        var json = Encoding.UTF8.GetString(decoded);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("claims", out var claimsElement)
            || claimsElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        string? objectId = null;
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

            if (objectId is null && ObjectIdClaimTypes.Contains(typ, StringComparer.OrdinalIgnoreCase))
            {
                objectId = val;
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

        if (string.IsNullOrEmpty(objectId))
        {
            return false;
        }

        principal = new ResolvedPrincipal(
            EntraObjectId: objectId,
            DisplayName: name ?? email ?? objectId,
            Email: email ?? string.Empty,
            PreferredLanguage: null);
        return true;
    }
}
