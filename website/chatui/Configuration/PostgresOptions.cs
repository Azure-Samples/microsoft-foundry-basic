using System.ComponentModel.DataAnnotations;

namespace chatui.Configuration;

public class PostgresOptions
{
    public const string SectionName = "Postgres";

    [Required]
    public string Host { get; init; } = default!;

    [Required]
    public string Database { get; init; } = default!;

    [Required]
    public string Username { get; init; } = default!;

    [Range(1, 65535)]
    public int Port { get; init; } = 5432;
}
