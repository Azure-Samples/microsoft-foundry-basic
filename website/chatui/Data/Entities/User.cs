namespace chatui.Data.Entities;

/// <summary>
/// A person signed into the workload via App Service Easy Auth (Entra ID).
/// The row must exist before the person can use the workload.
/// </summary>
public class User
{
    public Guid Id { get; set; }

    /// <summary>Object ID from the Easy Auth principal (aka Entra oid).</summary>
    public string EntraObjectId { get; set; } = default!;

    public string DisplayName { get; set; } = default!;

    public string Email { get; set; } = default!;

    /// <summary>BCP-47-ish language code the person prefers to read/hear translations in.</summary>
    public string PreferredLanguage { get; set; } = "en";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
