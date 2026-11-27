using Azure;
using Azure.Data.Tables;

namespace chatui.Data.Entities;

internal sealed class UserInvitationTableEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public string Email { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
    public Guid? ClaimedUserId { get; set; }
    public string? ClaimedProvider { get; set; }
    public string? ClaimedSubjectKey { get; set; }
}
