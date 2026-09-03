using chatui.Data.Entities;

namespace chatui.Auth;

public static class TrainingAccess
{
    private static readonly HashSet<string> MemberObjectIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "7bc3b904-679f-44a7-b0bf-fb6faf468a45",
    };

    public static bool IsMember(User user) => MemberObjectIds.Contains(user.EntraObjectId);
}