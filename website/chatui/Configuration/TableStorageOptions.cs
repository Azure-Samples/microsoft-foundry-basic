using System.ComponentModel.DataAnnotations;

namespace chatui.Configuration;

public sealed class TableStorageOptions
{
    public const string SectionName = "TableStorage";

    [Required, Url]
    public string Endpoint { get; init; } = string.Empty;

    [Required, MinLength(3)]
    public string UsersTableName { get; init; } = "users";

    [Required, MinLength(3)]
    public string UserInvitesTableName { get; init; } = "userinvites";
}
