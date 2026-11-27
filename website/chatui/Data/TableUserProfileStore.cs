using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Data.Tables;
using chatui.Auth;
using chatui.Configuration;
using chatui.Data.Entities;
using Microsoft.Extensions.Options;

namespace chatui.Data;

public sealed class TableUserProfileStore(
    TableServiceClient serviceClient,
    IOptions<TableStorageOptions> options,
    ILogger<TableUserProfileStore> logger) : IUserProfileStore
{
    private readonly TableClient users = serviceClient.GetTableClient(options.Value.UsersTableName);
    private readonly TableClient invitations = serviceClient.GetTableClient(options.Value.UserInvitesTableName);

    public async Task<User?> GetOrClaimAsync(ResolvedPrincipal principal, CancellationToken cancellationToken)
    {
        var rowKey = IdentityKey(principal.Provider, principal.Subject);
        var existing = await users.GetEntityIfExistsAsync<UserProfileTableEntity>(
            principal.Provider, rowKey, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (existing.HasValue)
        {
            return ToUser(existing.Value!);
        }

        if (string.IsNullOrWhiteSpace(principal.Email))
        {
            return null;
        }

        var invitation = await FindInvitationAsync(principal.Provider, principal.Email, cancellationToken).ConfigureAwait(false);
        if (invitation is null || invitation.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
        {
            return null;
        }

        Guid userId;
        if (invitation.ClaimedAt is not null)
        {
            if (!string.Equals(invitation.ClaimedProvider, principal.Provider, StringComparison.Ordinal)
                || !string.Equals(invitation.ClaimedSubjectKey, rowKey, StringComparison.Ordinal)
                || invitation.ClaimedUserId is not { } claimedUserId)
            {
                return null;
            }

            userId = claimedUserId;
        }
        else
        {
            userId = Guid.NewGuid();
            invitation.ClaimedAt = DateTimeOffset.UtcNow;
            invitation.ClaimedUserId = userId;
            invitation.ClaimedProvider = principal.Provider;
            invitation.ClaimedSubjectKey = rowKey;

            try
            {
                await invitations.UpdateEntityAsync(invitation, invitation.ETag, TableUpdateMode.Replace, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (RequestFailedException exception) when (exception.Status == StatusCodes.Status412PreconditionFailed)
            {
                logger.LogInformation("An invitation claim raced with another authenticated request.");
                return await GetOrClaimAsync(principal, cancellationToken).ConfigureAwait(false);
            }
        }

        var now = DateTimeOffset.UtcNow;
        var entity = new UserProfileTableEntity
        {
            PartitionKey = principal.Provider,
            RowKey = rowKey,
            Id = userId,
            Provider = principal.Provider,
            Subject = principal.Subject,
            Issuer = principal.Issuer,
            DisplayName = principal.DisplayName,
            Email = principal.Email,
            PreferredLanguage = principal.PreferredLanguage ?? "en",
            CreatedAt = now,
            UpdatedAt = now,
        };

        try
        {
            await users.AddEntityAsync(entity, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Provisioned an invited {Provider} user profile.", principal.Provider);
            return ToUser(entity);
        }
        catch (RequestFailedException exception) when (exception.Status == StatusCodes.Status409Conflict)
        {
            var raced = await users.GetEntityIfExistsAsync<UserProfileTableEntity>(
                principal.Provider, rowKey, cancellationToken: cancellationToken).ConfigureAwait(false);
            return raced.HasValue ? ToUser(raced.Value!) : null;
        }
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var filter = $"Id eq guid'{id:D}'";
        await foreach (var entity in users.QueryAsync<UserProfileTableEntity>(filter, maxPerPage: 1, cancellationToken: cancellationToken))
        {
            return ToUser(entity);
        }

        return null;
    }

    public async Task<IReadOnlyList<User>> SearchAsync(
        Guid excludeUserId,
        string? query,
        int limit,
        CancellationToken cancellationToken)
    {
        var normalizedQuery = query?.Trim();
        var results = new List<User>(limit);
        var inspected = 0;

        await foreach (var entity in users.QueryAsync<UserProfileTableEntity>(maxPerPage: 100, cancellationToken: cancellationToken))
        {
            if (++inspected > 1000)
            {
                break;
            }

            if (entity.Id == excludeUserId
                || !string.IsNullOrEmpty(normalizedQuery)
                    && !entity.DisplayName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                    && !entity.Email.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            results.Add(ToUser(entity));
            if (results.Count == limit)
            {
                break;
            }
        }

        return results.OrderBy(user => user.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task UpdateAsync(User user, CancellationToken cancellationToken)
    {
        var entity = FromUser(user);
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await users.UpdateEntityAsync(entity, ETag.All, TableUpdateMode.Replace, cancellationToken).ConfigureAwait(false);
        user.UpdatedAt = entity.UpdatedAt;
    }

    internal static string IdentityKey(string provider, string subject) => Hash($"{provider}|{subject}");

    internal static string InvitationKey(string email) => Hash(email.Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant());

    private async Task<UserInvitationTableEntity?> FindInvitationAsync(
        string provider,
        string email,
        CancellationToken cancellationToken)
    {
        var rowKey = InvitationKey(email);
        foreach (var partitionKey in new[] { provider, "any" })
        {
            var result = await invitations.GetEntityIfExistsAsync<UserInvitationTableEntity>(
                partitionKey, rowKey, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (result.HasValue)
            {
                return result.Value;
            }
        }

        return null;
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static User ToUser(UserProfileTableEntity entity) => new()
    {
        Id = entity.Id,
        Provider = entity.Provider,
        Subject = entity.Subject,
        Issuer = entity.Issuer,
        EntraObjectId = entity.Provider == IdentityProviders.Microsoft ? entity.Subject : string.Empty,
        DisplayName = entity.DisplayName,
        Email = entity.Email,
        PreferredLanguage = entity.PreferredLanguage,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt,
    };

    private static UserProfileTableEntity FromUser(User user) => new()
    {
        PartitionKey = user.Provider,
        RowKey = IdentityKey(user.Provider, user.Subject),
        Id = user.Id,
        Provider = user.Provider,
        Subject = user.Subject,
        Issuer = user.Issuer,
        DisplayName = user.DisplayName,
        Email = user.Email,
        PreferredLanguage = user.PreferredLanguage,
        CreatedAt = user.CreatedAt,
        UpdatedAt = user.UpdatedAt,
    };
}
