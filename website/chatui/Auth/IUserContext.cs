using chatui.Data.Entities;

namespace chatui.Auth;

/// <summary>
/// Resolves the currently signed-in user for the incoming HTTP request and
/// guarantees a corresponding row in the <c>users</c> table (upsert on first
/// hit). Every repository method that accesses tenant data should call this
/// first and scope its query by the returned <see cref="User.Id"/>.
/// </summary>
public interface IUserContext
{
    /// <summary>
    /// Returns the current user, creating them on first sign-in.
    /// Throws <see cref="UnauthorizedAccessException"/> if there is no
    /// authenticated principal on the request.
    /// </summary>
    Task<User> GetOrCreateCurrentAsync(CancellationToken cancellationToken = default);
}
