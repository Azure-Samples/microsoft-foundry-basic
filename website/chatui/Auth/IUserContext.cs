using chatui.Data.Entities;

namespace chatui.Auth;

/// <summary>
/// Resolves the currently signed-in user for the incoming HTTP request and
/// requires a corresponding row in the <c>users</c> table. Every repository
/// method that accesses tenant data should call this first and scope its query
/// by the returned <see cref="User.Id"/>.
/// </summary>
public interface IUserContext
{
    /// <summary>
    /// Returns the current pre-provisioned user.
    /// Throws <see cref="UnauthorizedAccessException"/> if there is no
    /// authenticated principal, or a user-context exception if demo persona
    /// selection is invalid.
    /// </summary>
    Task<User> GetCurrentAsync(CancellationToken cancellationToken = default);
}
