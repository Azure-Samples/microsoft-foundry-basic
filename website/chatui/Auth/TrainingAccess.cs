using chatui.Data.Entities;

namespace chatui.Auth;

public static class TrainingAccess
{
    public static bool IsMember(User user, IEnumerable<string> memberObjectIds) =>
        !string.IsNullOrWhiteSpace(user.EntraObjectId)
        && memberObjectIds.Contains(user.EntraObjectId, StringComparer.OrdinalIgnoreCase);
}