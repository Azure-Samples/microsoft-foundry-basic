namespace chatui.Data.Entities;

/// <summary>
/// A person signed into the workload through an allowed App Service identity provider.
/// The profile must exist or be created from an explicit invitation.
/// </summary>
public class User
{
    public Guid Id { get; set; }

    public string Provider { get; set; } = default!;

    public string Subject { get; set; } = default!;

    public string Issuer { get; set; } = default!;

    /// <summary>Compatibility value for migrated Microsoft training memberships.</summary>
    public string EntraObjectId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = default!;

    public string Email { get; set; } = default!;

    /// <summary>BCP-47-ish language code the person prefers to read/hear translations in.</summary>
    public string PreferredLanguage { get; set; } = "en";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
