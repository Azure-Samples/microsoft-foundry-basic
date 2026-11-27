namespace chatui.Data.Entities;

/// <summary>
/// One utterance in a meeting: the speaker's original text plus per-listener
/// translations stored in <see cref="TurnTranslation"/>. Audio is never persisted.
/// </summary>
public class Turn
{
    public Guid Id { get; set; }

    public Guid MeetingId { get; set; }

    public Guid SpeakerId { get; set; }

    /// <summary>
    /// Optional: the participant the speaker was addressing, as inferred by the
    /// Foundry addressee-hint agent. Used only for UX emphasis; not for delivery.
    /// </summary>
    public Guid? AddresseeId { get; set; }

    public string SourceLang { get; set; } = default!;

    public string Original { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }

    public Meeting Meeting { get; set; } = default!;

    public User Speaker { get; set; } = default!;

    public User? Addressee { get; set; }

    public ICollection<TurnTranslation> Translations { get; set; } = new List<TurnTranslation>();
}
