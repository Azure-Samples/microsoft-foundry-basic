namespace chatui.Data.Entities;

/// <summary>
/// Materialised translation of a <see cref="Turn"/> for a specific listener language.
/// Kept so participants can re-read history in their language after the meeting.
/// </summary>
public class TurnTranslation
{
    public Guid TurnId { get; set; }

    public string TargetLang { get; set; } = default!;

    public string Translated { get; set; } = default!;

    public Turn Turn { get; set; } = default!;
}
