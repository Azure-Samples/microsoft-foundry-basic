namespace chatui.Data.Entities;

/// <summary>
/// A user joined to a specific meeting with a chosen listening language.
/// Language may change during the meeting; the client re-joins to update it
/// so its Web PubSub subscription switches to the new language group.
/// </summary>
public class Participant
{
    public Guid Id { get; set; }

    public Guid MeetingId { get; set; }

    public Guid UserId { get; set; }

    public string Language { get; set; } = default!;

    /// <summary>'initiator' or 'listener'.</summary>
    public string Role { get; set; } = "listener";

    public DateTimeOffset JoinedAt { get; set; }

    public DateTimeOffset? LeftAt { get; set; }

    public Meeting Meeting { get; set; } = default!;

    public User User { get; set; } = default!;
}
