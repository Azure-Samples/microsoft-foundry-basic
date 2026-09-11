using System.Text.Json;
using chatui.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace chatui.Data;

internal static class DatabaseInitialization
{
    internal static async Task RunAsync(string inputPath)
    {
        InitializationInput input;
        try
        {
            input = JsonSerializer.Deserialize<InitializationInput>(await File.ReadAllTextAsync(inputPath))
                ?? throw new InvalidOperationException("Missing initialization input.");
        }
        finally
        {
            File.Delete(inputPath);
        }

        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = input.Host,
            Database = input.Database,
            Username = input.AdminLogin,
            Password = input.AdminToken,
            SslMode = SslMode.VerifyFull,
        }.ToString();
        var options = new DbContextOptionsBuilder<TranslatorDbContext>()
            .UseNpgsql(connectionString, postgres => postgres.UseVector())
            .UseSnakeCaseNamingConvention().Options;
        await using var context = new TranslatorDbContext(options);
        await context.Database.MigrateAsync();

        var roleConnectionString = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" }.ToString();
        await using var connection = new NpgsqlConnection(roleConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var exists = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_roles WHERE rolname = $1)", connection, transaction);
        exists.Parameters.AddWithValue(input.AppClientId.ToString());
        if (!Equals(await exists.ExecuteScalarAsync(), true))
        {
            await using var create = new NpgsqlCommand(
                "SELECT * FROM pgaadauth_create_principal_with_oid($1, $2, 'service', false, false)", connection, transaction);
            create.Parameters.AddWithValue(input.AppClientId.ToString());
            create.Parameters.AddWithValue(input.AppObjectId.ToString());
            await create.ExecuteNonQueryAsync();
        }
        using var identifiers = new NpgsqlCommandBuilder();
        var appRole = identifiers.QuoteIdentifier(input.AppClientId.ToString());
        var database = identifiers.QuoteIdentifier(input.Database);
        await transaction.CommitAsync();
        await using var applicationConnection = new NpgsqlConnection(connectionString);
        await applicationConnection.OpenAsync();
        await using var grant = new NpgsqlCommand($"""
            GRANT CONNECT ON DATABASE {database} TO {appRole};
            GRANT USAGE ON SCHEMA public TO {appRole};
            GRANT SELECT, INSERT, UPDATE, DELETE ON public.users TO {appRole};
            """, applicationConnection);
        await grant.ExecuteNonQueryAsync();

        var objectId = input.UserObjectId.ToString();
        if (!await context.Users.AnyAsync(user => user.EntraObjectId == objectId))
        {
            context.Users.Add(new User
            {
                EntraObjectId = objectId,
                DisplayName = input.UserDisplayName,
                Email = input.UserEmail,
                PreferredLanguage = "en",
            });
            await context.SaveChangesAsync();
        }
        Console.WriteLine("Database initialized; managed identity has users-table DML only; selected user provisioned.");
    }

    private sealed record InitializationInput(
        string Host, string Database, string AdminLogin, string AdminToken,
        Guid AppClientId, Guid AppObjectId, Guid UserObjectId, string UserDisplayName, string UserEmail);
}