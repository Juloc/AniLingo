using Jularr.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Auth;

public sealed record AccountGroupSummary(
    string Id,
    string Name,
    int MemberCount,
    DateTime CreatedAt);

internal sealed class AccountGroupQueryRow
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int MemberCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Relational account-group membership. Groups are stable policy selectors; membership alone does
/// not grant authorization. Feature policies explicitly decide whether and how a group is used.
/// </summary>
public sealed class AccountGroupStore(AppDbContext db)
{
    public async Task<IReadOnlyList<AccountGroupSummary>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await db.Database
            .SqlQueryRaw<AccountGroupQueryRow>(
                """
                SELECT
                    account_group."Id",
                    account_group."Name",
                    CAST(COUNT(account_group_member."AccountId") AS INTEGER) AS "MemberCount",
                    account_group."CreatedAt"
                FROM "AccountGroups" AS account_group
                LEFT JOIN "AccountGroupMembers" AS account_group_member
                    ON account_group_member."GroupId" = account_group."Id"
                GROUP BY
                    account_group."Id",
                    account_group."Name",
                    account_group."CreatedAt"
                ORDER BY account_group."Name"
                """)
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new AccountGroupSummary(
                row.Id,
                row.Name,
                row.MemberCount,
                row.CreatedAt))
            .ToArray();
    }

    public async Task<IReadOnlySet<string>> GetMembershipIdsAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        var ids = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT account_group_member."GroupId" AS "Value"
                FROM "AccountGroupMembers" AS account_group_member
                WHERE account_group_member."AccountId" = {0}
                ORDER BY account_group_member."GroupId"
                """,
                accountId)
            .ToListAsync(cancellationToken);

        return ids.ToHashSet(StringComparer.Ordinal);
    }

    public async Task<AccountGroupSummary> CreateAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var cleanName = CleanName(name);
        var normalizedName = NormalizeName(cleanName);

        if (await ExistsByNormalizedNameAsync(normalizedName, excludeId: null, cancellationToken))
        {
            throw new InvalidOperationException("A group with this name already exists.");
        }

        var id = Guid.NewGuid().ToString("N");
        var createdAt = DateTime.UtcNow;

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "AccountGroups" (
                "Id",
                "Name",
                "NormalizedName",
                "CreatedAt")
            VALUES ({0}, {1}, {2}, {3})
            """,
            new object[] { id, cleanName, normalizedName, createdAt },
            cancellationToken);

        return new AccountGroupSummary(id, cleanName, 0, createdAt);
    }

    public async Task RenameAsync(
        string groupId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        var cleanName = CleanName(name);
        var normalizedName = NormalizeName(cleanName);

        if (await ExistsByNormalizedNameAsync(normalizedName, groupId, cancellationToken))
        {
            throw new InvalidOperationException("A group with this name already exists.");
        }

        var changed = await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE "AccountGroups"
            SET
                "Name" = {0},
                "NormalizedName" = {1}
            WHERE "Id" = {2}
            """,
            new object[] { cleanName, normalizedName, groupId },
            cancellationToken);

        if (changed == 0)
        {
            throw new InvalidOperationException("Group was not found.");
        }
    }

    public async Task DeleteAsync(
        string groupId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        var changed = await db.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM "AccountGroups"
            WHERE "Id" = {0}
            """,
            new object[] { groupId },
            cancellationToken);

        if (changed == 0)
        {
            throw new InvalidOperationException("Group was not found.");
        }
    }

    public async Task SetMembershipsAsync(
        string accountId,
        IEnumerable<string> groupIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentNullException.ThrowIfNull(groupIds);

        var account = await db.OwnerAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == accountId,
                cancellationToken)
            ?? throw new InvalidOperationException("Account was not found.");

        if (account.Role == AccountRole.Owner)
        {
            throw new InvalidOperationException("The owner account does not use group memberships.");
        }

        var selectedIds = groupIds
            .Where(groupId => !string.IsNullOrWhiteSpace(groupId))
            .Select(groupId => groupId.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (selectedIds.Length > 0)
        {
            var knownIds = (await ListAsync(cancellationToken))
                .Select(group => group.Id)
                .ToHashSet(StringComparer.Ordinal);

            if (selectedIds.Any(groupId => !knownIds.Contains(groupId)))
            {
                throw new InvalidOperationException("One or more selected groups no longer exist.");
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM "AccountGroupMembers"
            WHERE "AccountId" = {0}
            """,
            new object[] { accountId },
            cancellationToken);

        foreach (var groupId in selectedIds)
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "AccountGroupMembers" (
                    "GroupId",
                    "AccountId")
                VALUES ({0}, {1})
                ON CONFLICT DO NOTHING
                """,
                new object[] { groupId, accountId },
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<bool> ExistsByNormalizedNameAsync(
        string normalizedName,
        string? excludeId,
        CancellationToken cancellationToken)
    {
        var ids = excludeId is null
            ? await db.Database
                .SqlQueryRaw<string>(
                    """
                    SELECT account_group."Id" AS "Value"
                    FROM "AccountGroups" AS account_group
                    WHERE account_group."NormalizedName" = {0}
                    LIMIT 1
                    """,
                    normalizedName)
                .ToListAsync(cancellationToken)
            : await db.Database
                .SqlQueryRaw<string>(
                    """
                    SELECT account_group."Id" AS "Value"
                    FROM "AccountGroups" AS account_group
                    WHERE account_group."NormalizedName" = {0}
                        AND account_group."Id" <> {1}
                    LIMIT 1
                    """,
                    normalizedName,
                    excludeId)
                .ToListAsync(cancellationToken);

        return ids.Count > 0;
    }

    private static string CleanName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var cleaned = name.Trim();
        if (cleaned.Length > 80)
        {
            throw new ArgumentException(
                "Group name must be 80 characters or fewer.",
                nameof(name));
        }

        return cleaned;
    }

    private static string NormalizeName(string name) =>
        name.ToUpperInvariant();
}
