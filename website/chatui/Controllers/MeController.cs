using System.ComponentModel.DataAnnotations;
using chatui.Auth;
using chatui.Data;
using Microsoft.AspNetCore.Mvc;

namespace chatui.Controllers;

[ApiController]
[Route("api/me")]
public class MeController(IUserContext userContext, TranslatorDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MeResponse>> Get(CancellationToken cancellationToken)
    {
        var user = await userContext.GetOrCreateCurrentAsync(cancellationToken).ConfigureAwait(false);
        return Ok(new MeResponse(user.Id, user.DisplayName, user.Email, user.PreferredLanguage));
    }

    [HttpPatch]
    public async Task<ActionResult<MeResponse>> Patch(
        [FromBody] MePatchRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userContext.GetOrCreateCurrentAsync(cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(request.PreferredLanguage))
        {
            user.PreferredLanguage = request.PreferredLanguage!;
            user.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Ok(new MeResponse(user.Id, user.DisplayName, user.Email, user.PreferredLanguage));
    }

    public sealed record MeResponse(Guid Id, string DisplayName, string Email, string PreferredLanguage);

    public sealed record MePatchRequest([StringLength(16, MinimumLength = 2)] string? PreferredLanguage);
}
