using Pgvector;

namespace chatui.Data.Entities;

/// <summary>
/// A single personal-chat message: original in the sender's language plus one
/// pre-computed translation targeted at the recipient's language. Embedding is
/// optional, back-filled asynchronously for semantic search over history.
/// </summary>
public class Message
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    public Guid FromUserId { get; set; }

    public string SourceLang { get; set; } = default!;

    public string TargetLang { get; set; } = default!;

    public string Original { get; set; } = default!;

    public string Translated { get; set; } = default!;

    public Vector? Embedding { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Conversation Conversation { get; set; } = default!;

    public User FromUser { get; set; } = default!;
}
