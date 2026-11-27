extern alias AzureIdentity;

using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using DefaultAzureCredential = AzureIdentity::Azure.Identity.DefaultAzureCredential;
using ManagedIdentityCredential = AzureIdentity::Azure.Identity.ManagedIdentityCredential;
using ManagedIdentityId = AzureIdentity::Azure.Identity.ManagedIdentityId;
using Npgsql;

const string MicrosoftProvider = "microsoft";
const string PostgresScope = "https://ossrdbms-aad.database.windows.net/.default";

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintUsage();
    return 0;
}

try
{
    var options = ParseOptions(args.Skip(1));
    var credential = CreateCredential();
    var endpoint = Required(options, "table-endpoint");
    var service = new TableServiceClient(new Uri(endpoint), credential);

    return args[0].ToLowerInvariant() switch
    {
        "migrate" => await MigrateAsync(service, credential, options),
        "invite" => await InviteAsync(service, options),
        _ => throw new ArgumentException($"Unknown command '{args[0]}'."),
    };
}
catch (Exception exception) when (exception is ArgumentException or UriFormatException or FormatException)
{
    Console.Error.WriteLine($"Input error: {exception.Message}");
    PrintUsage();
    return 2;
}

static async Task<int> MigrateAsync(
    TableServiceClient service,
    TokenCredential credential,
    IReadOnlyDictionary<string, string> options)
{
    var usersTable = service.GetTableClient(Get(options, "users-table", "users"));
    var token = await credential.GetTokenAsync(
        new TokenRequestContext([PostgresScope]), CancellationToken.None);
    var connectionString = new NpgsqlConnectionStringBuilder
    {
        Host = Required(options, "postgres-host"),
        Database = Required(options, "postgres-database"),
        Username = Required(options, "postgres-user"),
        Password = token.Token,
        SslMode = SslMode.VerifyFull,
        Timeout = 15,
        CommandTimeout = 30,
    }.ConnectionString;

    var sourceCount = 0;
    var createdCount = 0;
    var existingCount = 0;
    var failedCount = 0;

    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(
        "SELECT id, entra_object_id, display_name, email, preferred_language, created_at, updated_at FROM users ORDER BY id",
        connection);
    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        sourceCount++;
        try
        {
            var sourceId = reader.GetGuid(0);
            var subject = reader.GetString(1);
            var entity = new TableEntity(MicrosoftProvider, IdentityKey(MicrosoftProvider, subject))
            {
                ["Id"] = sourceId,
                ["Provider"] = MicrosoftProvider,
                ["Subject"] = subject,
                ["Issuer"] = "migrated-entra-id",
                ["DisplayName"] = reader.GetString(2),
                ["Email"] = reader.GetString(3),
                ["PreferredLanguage"] = reader.GetString(4),
                ["CreatedAt"] = reader.GetFieldValue<DateTimeOffset>(5),
                ["UpdatedAt"] = reader.GetFieldValue<DateTimeOffset>(6),
            };

            var response = await usersTable.GetEntityIfExistsAsync<TableEntity>(entity.PartitionKey, entity.RowKey);
            if (response.HasValue)
            {
                if (ProfileMatches(response.Value!, sourceId, subject))
                {
                    existingCount++;
                }
                else
                {
                    failedCount++;
                }
                continue;
            }

            await usersTable.AddEntityAsync(entity);
            createdCount++;
        }
        catch (Exception exception) when (exception is RequestFailedException or NpgsqlException or InvalidCastException)
        {
            failedCount++;
        }
    }

    Console.WriteLine(
        "Migration summary: source={0}, created={1}, existing={2}, failed={3}, verified={4}",
        sourceCount, createdCount, existingCount, failedCount,
        failedCount == 0 && sourceCount == createdCount + existingCount);
    return failedCount == 0 && sourceCount == createdCount + existingCount ? 0 : 1;
}

static async Task<int> InviteAsync(
    TableServiceClient service,
    IReadOnlyDictionary<string, string> options)
{
    var email = Required(options, "email").Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant();
    var provider = Get(options, "provider", "any").ToLowerInvariant();
    if (provider is not ("any" or "microsoft" or "google"))
    {
        throw new ArgumentException("--provider must be any, microsoft, or google.");
    }

    DateTimeOffset? expiresAt = options.TryGetValue("expires", out var rawExpiry)
        ? DateTimeOffset.Parse(rawExpiry, null, System.Globalization.DateTimeStyles.RoundtripKind)
        : null;
    var table = service.GetTableClient(Get(options, "invites-table", "userinvites"));
    var rowKey = InvitationKey(email);
    var current = await table.GetEntityIfExistsAsync<TableEntity>(provider, rowKey);
    if (current.HasValue && current.Value!.ContainsKey("ClaimedAt"))
    {
        throw new ArgumentException("The matching invitation has already been claimed and cannot be replaced.");
    }

    var entity = new TableEntity(provider, rowKey)
    {
        ["Email"] = email,
        ["CreatedAt"] = current.HasValue && current.Value!.TryGetValue("CreatedAt", out var createdAt)
            ? createdAt
            : DateTimeOffset.UtcNow,
    };
    if (expiresAt is not null)
    {
        entity["ExpiresAt"] = expiresAt;
    }
    await table.UpsertEntityAsync(entity, TableUpdateMode.Replace);
    Console.WriteLine("Invitation stored for provider={0}, expires={1}.", provider, expiresAt?.ToString("O") ?? "never");
    return 0;
}

static TokenCredential CreateCredential()
{
    var clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
    return string.IsNullOrWhiteSpace(clientId)
        ? new DefaultAzureCredential()
        : new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId));
}

static Dictionary<string, string> ParseOptions(IEnumerable<string> values)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    using var enumerator = values.GetEnumerator();
    while (enumerator.MoveNext())
    {
        var key = enumerator.Current;
        if (!key.StartsWith("--", StringComparison.Ordinal) || !enumerator.MoveNext())
        {
            throw new ArgumentException($"Expected --name value, received '{key}'.");
        }
        result[key[2..]] = enumerator.Current;
    }
    return result;
}

static string Required(IReadOnlyDictionary<string, string> options, string name) =>
    options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ArgumentException($"Missing --{name}.");

static string Get(IReadOnlyDictionary<string, string> options, string name, string fallback) =>
    options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

static bool ProfileMatches(TableEntity entity, Guid sourceId, string subject) =>
    entity.TryGetValue("Id", out var id) && id is Guid existingId && existingId == sourceId
    && entity.TryGetValue("Provider", out var provider) && Equals(provider, MicrosoftProvider)
    && entity.TryGetValue("Subject", out var existingSubject) && Equals(existingSubject, subject);

static string IdentityKey(string provider, string subject) => Hash($"{provider}|{subject}");
static string InvitationKey(string email) => Hash(email.Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant());
static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

static void PrintUsage()
{
    Console.WriteLine("""
        User profile administration (authenticates with DefaultAzureCredential; no keys or connection strings).

        migrate --table-endpoint <uri> --postgres-host <host> --postgres-database <db> --postgres-user <login>
                [--users-table users]
        invite  --table-endpoint <uri> --email <address> [--provider any|microsoft|google]
                [--expires <ISO-8601>] [--invites-table userinvites]
        """);
}
