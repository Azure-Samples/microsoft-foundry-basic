using Azure.Core;
using chatui.Auth;
using chatui.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace chatui.Controllers;

/// <summary>
/// Mints a short-lived bearer token that the browser Speech SDK uses via
/// <c>SpeechConfig.FromAuthorizationToken(token, region)</c>. The token
/// format is <c>aad#{resourceId}#{accessToken}</c> — the SDK unwraps it
/// and sends the inner JWT to the Speech Translation service. Speech is
/// deployed with <c>disableLocalAuth: true</c> so we never expose keys.
/// </summary>
[ApiController]
[Route("api/speech")]
public class SpeechController(
    IUserContext userContext,
    TokenCredential credential,
    IOptions<SpeechOptions> speechOptions) : ControllerBase
{
    private static readonly string[] CognitiveServicesScopes =
        ["https://cognitiveservices.azure.com/.default"];

    [HttpGet("token")]
    public async Task<ActionResult<SpeechTokenResponse>> GetToken(CancellationToken ct)
    {
        // Force the sign-in gate + user provisioning; the client only calls this
        // when it's about to talk to the Speech SDK from a signed-in session.
        _ = await userContext.GetOrCreateCurrentAsync(ct).ConfigureAwait(false);

        var accessToken = await credential
            .GetTokenAsync(new TokenRequestContext(CognitiveServicesScopes), ct)
            .ConfigureAwait(false);

        var opts = speechOptions.Value;
        var wrapped = $"aad#{opts.ResourceId}#{accessToken.Token}";

        return Ok(new SpeechTokenResponse(wrapped, opts.Region, accessToken.ExpiresOn));
    }

    public sealed record SpeechTokenResponse(string Token, string Region, DateTimeOffset ExpiresAt);
}
