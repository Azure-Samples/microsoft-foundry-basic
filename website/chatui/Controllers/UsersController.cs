using chatui.Auth;
using chatui.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace chatui.Controllers;

/// <summary>
/// Directory of users that have already signed into the workload.
/// Discovery of tenant users who haven't signed in yet requires Microsoft
/// Graph consent — deferred until a later phase. For MVP, users show up
/// here after their first hit on /api/me.
/// </summary>
[ApiController]
[Route("api/users")]
public class UsersController(IUserContext userContext, TranslatorDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserSummary>>> Search(
        [FromQuery] string? query,
        [FromQuery] int limit = 25,
        CancellationToken cancellationToken = default)
    {
        var me = await userContext.GetOrCreateCurrentAsync(cancellationToken).ConfigureAwait(false);

        limit = Math.Clamp(limit, 1, 100);
        query = query?.Trim();

        var q = dbContext.Users.AsNoTracking().Where(u => u.Id != me.Id);
        if (!string.IsNullOrEmpty(query))
        {
            var like = $"%{query}%";
            q = q.Where(u => EF.Functions.ILike(u.DisplayName, like) || EF.Functions.ILike(u.Email, like));
        }

        var results = await q
            .OrderBy(u => u.DisplayName)
            .Take(limit)
            .Select(u => new UserSummary(u.Id, u.DisplayName, u.Email, u.PreferredLanguage))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Ok(results);
    }

    public sealed record UserSummary(Guid Id, string DisplayName, string Email, string PreferredLanguage);
}
