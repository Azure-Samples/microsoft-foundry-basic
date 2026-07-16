namespace chatui.Data.Entities;

/// <summary>
/// An on-site meeting where participants converse across languages in real time.
/// Turns hang off this row; join tokens are stateless JWTs (see JoinTokenService)
/// so a meeting doesn't need a token table — revocation is by setting Status='ended'.
/// </summary>
public class Meeting
{
    public Guid Id { get; set; }

    public Guid InitiatorId { get; set; }

    public string? Title { get; set; }

    /// <summary>'active' or 'ended'. Enforced by a check constraint in the DB.</summary>
    public string Status { get; set; } = "active";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    public User Initiator { get; set; } = default!;

    public ICollection<Participant> Participants { get; set; } = new List<Participant>();

    public ICollection<Turn> Turns { get; set; } = new List<Turn>();
}
