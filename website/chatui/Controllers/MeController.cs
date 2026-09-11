using System.ComponentModel.DataAnnotations;
using chatui.Auth;
using chatui.Data;
using chatui.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace chatui.Controllers;

[ApiController]
[Route("api/me")]
public class MeController(IUserContext userContext, TranslatorDbContext dbContext, IOptions<ChatApiOptions> options) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<MeResponse>> Get(CancellationToken cancellationToken)
    {
        var user = await userContext.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        return Ok(ToResponse(user));
    }

    [HttpPatch]
    public async Task<ActionResult<MeResponse>> Patch(
        [FromBody] MePatchRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userContext.GetCurrentAsync(cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(request.PreferredLanguage))
        {
            user.PreferredLanguage = request.PreferredLanguage!;
            user.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Ok(ToResponse(user));
    }

    private MeResponse ToResponse(Data.Entities.User user) => new(
        user.Id,
        user.DisplayName,
        user.Email,
        user.PreferredLanguage,
        TrainingAccess.IsMember(user, options.Value.TrainingMemberObjectIds),
        TrainingAccess.IsMember(user, options.Value.TrainingMemberObjectIds) && options.Value.TrainingGuidedEnabled);

    public sealed record MeResponse(
        Guid Id,
        string DisplayName,
        string Email,
        string PreferredLanguage,
        bool CanUseTraining,
        bool CanUseGuidedTraining);

    public sealed record MePatchRequest([StringLength(16, MinimumLength = 2)] string? PreferredLanguage);
}
