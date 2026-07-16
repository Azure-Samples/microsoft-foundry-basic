namespace chatui.Data.Entities;

/// <summary>
/// Foundry-generated summary of a meeting, cached per requesting language.
/// One row per (MeetingId, Language).
/// </summary>
public class MeetingSummary
{
    public Guid MeetingId { get; set; }

    public string Language { get; set; } = default!;

    public string Summary { get; set; } = default!;

    public DateTimeOffset GeneratedAt { get; set; }

    public Meeting Meeting { get; set; } = default!;
}
