using chatui.Auth;
using chatui.Data.Entities;

namespace chatui.Data;

public interface IUserProfileStore
{
    Task<User?> GetOrClaimAsync(ResolvedPrincipal principal, CancellationToken cancellationToken);
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<User>> SearchAsync(Guid excludeUserId, string? query, int limit, CancellationToken cancellationToken);
    Task UpdateAsync(User user, CancellationToken cancellationToken);
}
