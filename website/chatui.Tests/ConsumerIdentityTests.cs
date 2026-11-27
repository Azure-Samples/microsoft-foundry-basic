using System.Text;
using System.Text.Json;
using chatui.Auth;
using chatui.Data;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace chatui.Tests;

public sealed class ConsumerIdentityTests
{
    [Fact]
    public void MicrosoftPrincipalPrefersObjectId()
    {
        var principal = Parse("aad", ("oid", "object-id"), ("sub", "pairwise-subject"), ("email", "user@outlook.com"));

        Assert.Equal(IdentityProviders.Microsoft, principal.Provider);
        Assert.Equal("object-id", principal.Subject);
        Assert.Equal("user@outlook.com", principal.Email);
    }

    [Fact]
    public void MicrosoftPrincipalFallsBackToSubject()
    {
        var principal = Parse("aad", ("sub", "personal-account-subject"));

        Assert.Equal(IdentityProviders.Microsoft, principal.Provider);
        Assert.Equal("personal-account-subject", principal.Subject);
    }

    [Fact]
    public void GooglePrincipalUsesSubject()
    {
        var principal = Parse("google", ("sub", "google-subject"), ("oid", "ignored"));

        Assert.Equal(IdentityProviders.Google, principal.Provider);
        Assert.Equal("google-subject", principal.Subject);
    }

    [Fact]
    public void ProviderKeysKeepProfilesSeparate()
    {
        var microsoftKey = TableUserProfileStore.IdentityKey(IdentityProviders.Microsoft, "same-subject");
        var googleKey = TableUserProfileStore.IdentityKey(IdentityProviders.Google, "same-subject");

        Assert.NotEqual(microsoftKey, googleKey);
        Assert.Equal(microsoftKey, TableUserProfileStore.IdentityKey(IdentityProviders.Microsoft, "same-subject"));
    }

    [Fact]
    public void InvitationKeysNormalizeEmail()
    {
        Assert.Equal(
            TableUserProfileStore.InvitationKey("Person@Example.com"),
            TableUserProfileStore.InvitationKey("  person@example.com  "));
    }

    [Fact]
    public void MalformedPrincipalIsRejected()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-MS-CLIENT-PRINCIPAL"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("not-json"));

        Assert.False(EasyAuthUserContext.TryReadEasyAuthPrincipal(context, out _));
    }

    private static ResolvedPrincipal Parse(string authType, params (string Type, string Value)[] claims)
    {
        var payload = JsonSerializer.Serialize(new
        {
            auth_typ = authType,
            claims = claims.Select(claim => new { typ = claim.Type, val = claim.Value }),
        });
        var context = new DefaultHttpContext();
        context.Request.Headers["X-MS-CLIENT-PRINCIPAL"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));

        Assert.True(EasyAuthUserContext.TryReadEasyAuthPrincipal(context, out var principal));
        return principal;
    }
}
