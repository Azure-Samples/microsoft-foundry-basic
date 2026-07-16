namespace chatui.Data.Entities;

/// <summary>
/// A 1:1 personal-chat thread between two users.
/// Users are stored in canonical order (UserAId &lt; UserBId) so (A,B) and (B,A)
/// resolve to the same conversation row.
/// </summary>
public class Conversation
{
    public Guid Id { get; set; }

    public Guid UserAId { get; set; }

    public Guid UserBId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public User UserA { get; set; } = default!;

    public User UserB { get; set; } = default!;
}
