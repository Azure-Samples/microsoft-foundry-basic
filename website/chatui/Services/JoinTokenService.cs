using System.Text;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using chatui.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace chatui.Services;

/// <summary>
/// Issues and validates stateless HS256 JWTs used as meeting join tokens.
/// The signing key is fetched from Key Vault (see key-vault.bicep) and
/// cached in-memory for 5 minutes.
/// </summary>
public sealed class JoinTokenService
{
    private const string MeetingIdClaim = "mid";

    private readonly SecretClient _secretClient;
    private readonly IOptions<JoinTokenOptions> _options;
    private readonly IOptions<KeyVaultOptions> _kvOptions;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private CachedKey? _cachedKey;

    public JoinTokenService(
        TokenCredential credential,
        IOptions<JoinTokenOptions> options,
        IOptions<KeyVaultOptions> kvOptions)
    {
        _options = options;
        _kvOptions = kvOptions;
        _secretClient = new SecretClient(new Uri(kvOptions.Value.Uri), credential);
    }

    public async Task<string> IssueAsync(Guid meetingId, string issuer, CancellationToken cancellationToken = default)
    {
        var key = await GetSigningKeyAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Value.Issuer ?? issuer,
            Audience = _options.Value.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddHours(_options.Value.LifetimeHours),
            Subject = new System.Security.Claims.ClaimsIdentity(
            [
                new System.Security.Claims.Claim("sub", $"meeting:{meetingId}"),
                new System.Security.Claims.Claim(MeetingIdClaim, meetingId.ToString()),
                new System.Security.Claims.Claim("jti", Guid.NewGuid().ToString()),
            ]),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        };

        var handler = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false };
        return handler.CreateToken(descriptor);
    }

    public async Task<JoinTokenClaims> ValidateAsync(string token, CancellationToken cancellationToken = default)
    {
        var key = await GetSigningKeyAsync(cancellationToken).ConfigureAwait(false);
        var handler = new JsonWebTokenHandler();
        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer = false, // Issuer is the app's own base URI — informational.
            ValidateAudience = true,
            ValidAudience = _options.Value.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ClockSkew = TimeSpan.FromMinutes(1),
        };

        var result = await handler.ValidateTokenAsync(token, validationParams).ConfigureAwait(false);
        if (!result.IsValid)
        {
            throw new InvalidOperationException("Invalid meeting join token.", result.Exception);
        }

        if (!result.Claims.TryGetValue(MeetingIdClaim, out var midObj) || midObj is not string midStr
            || !Guid.TryParse(midStr, out var meetingId))
        {
            throw new InvalidOperationException("Meeting join token is missing the 'mid' claim.");
        }

        return new JoinTokenClaims(meetingId);
    }

    private async Task<SymmetricSecurityKey> GetSigningKeyAsync(CancellationToken cancellationToken)
    {
        var cached = _cachedKey;
        if (cached is not null && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.Key;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = _cachedKey;
            if (cached is not null && cached.ExpiresAt > DateTimeOffset.UtcNow)
            {
                return cached.Key;
            }

            var secret = await _secretClient
                .GetSecretAsync(_kvOptions.Value.JoinTokenSigningKeySecretName, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var keyBytes = Encoding.UTF8.GetBytes(secret.Value.Value);
            var symmetricKey = new SymmetricSecurityKey(keyBytes);
            _cachedKey = new CachedKey(symmetricKey, DateTimeOffset.UtcNow.AddMinutes(5));
            return symmetricKey;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public sealed record JoinTokenClaims(Guid MeetingId);

    private sealed record CachedKey(SymmetricSecurityKey Key, DateTimeOffset ExpiresAt);
}
