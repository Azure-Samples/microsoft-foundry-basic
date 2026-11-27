using chatui.Auth;
using chatui.Data;
using Microsoft.AspNetCore.Mvc;

namespace chatui.Controllers;

/// <summary>
/// Directory of active invited user profiles across configured identity providers.
/// </summary>
[ApiController]
[Route("api/users")]
public class UsersController(IUserContext userContext, IUserProfileStore profiles) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserSummary>>> Search(
        [FromQuery] string? query,
        [FromQuery] int limit = 25,
        CancellationToken cancellationToken = default)
    {
        var me = await userContext.GetCurrentAsync(cancellationToken).ConfigureAwait(false);

        limit = Math.Clamp(limit, 1, 100);
        query = query?.Trim();

        var users = await profiles.SearchAsync(me.Id, query, limit, cancellationToken).ConfigureAwait(false);
        var results = users
            .Select(user => new UserSummary(user.Id, user.DisplayName, user.Email, user.PreferredLanguage))
            .ToList();
        return Ok(results);
    }

    public sealed record UserSummary(Guid Id, string DisplayName, string Email, string PreferredLanguage);
}
