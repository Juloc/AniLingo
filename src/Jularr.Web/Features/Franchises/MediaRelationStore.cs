using System.Data;
using System.Data.Common;
using System.Globalization;
using Jularr.Web.Data;
using Jularr.Web.Features.Watchlist;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Franchises;

/// <summary>A directional provider relation: <see cref="To"/> is the <see cref="RelationType"/> of <see cref="From"/>.</summary>
public sealed record MediaRelation(
    WatchlistIdentity From,
    WatchlistIdentity To,
    string RelationType);

/// <summary>
/// The provider relation graph between works (table MediaRelations), written by the franchise
/// refresh. Rows that were rejected stay hidden.
/// </summary>
public sealed class MediaRelationStore(AppDbContext db)
{
    public Task UpsertProviderAsync(
        WatchlistIdentity from,
        WatchlistIdentity to,
        string relationType,
        string source,
        CancellationToken cancellationToken)
    {
        var normalizedType = NormalizeRelationType(relationType);
        var normalizedSource = NormalizeSource(source);
        var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        return WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO "MediaRelations"
                    ("Id", "FromMediaType", "FromProvider", "FromExternalId",
                     "ToMediaType", "ToProvider", "ToExternalId", "RelationType",
                     "Source", "Confidence", "ReviewState", "IsManual", "UpdatedAtUtc")
                VALUES
                    (@id, @fromType, @fromProvider, @fromId,
                     @toType, @toProvider, @toId, @relationType,
                     @source, 1.0, 'confirmed', 0, @updated)
                ON CONFLICT (
                    "FromMediaType", "FromProvider", "FromExternalId",
                    "ToMediaType", "ToProvider", "ToExternalId", "RelationType"
                ) DO UPDATE SET
                    "Source" = CASE
                        WHEN "MediaRelations"."IsManual" = 1 THEN "MediaRelations"."Source"
                        ELSE excluded."Source"
                    END,
                    "UpdatedAtUtc" = excluded."UpdatedAtUtc";
                """;
            BindIdentity(command, "from", from);
            BindIdentity(command, "to", to);
            Add(command, "@id", Guid.NewGuid().ToString("D"));
            Add(command, "@relationType", normalizedType);
            Add(command, "@source", normalizedSource);
            Add(command, "@updated", now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    /// <summary>Relations whose both ends are members of the franchise.</summary>
    public async Task<IReadOnlyList<MediaRelation>> GetForFranchiseAsync(
        Guid franchiseId,
        CancellationToken cancellationToken) =>
        await WithConnectionAsync<IReadOnlyList<MediaRelation>>(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT r."FromMediaType", r."FromProvider", r."FromExternalId",
                       r."ToMediaType", r."ToProvider", r."ToExternalId", r."RelationType"
                FROM "MediaRelations" r
                INNER JOIN "FranchiseMembers" a
                    ON a."FranchiseId" = @franchise
                   AND a."MediaType" = r."FromMediaType"
                   AND a."Provider" = r."FromProvider"
                   AND a."ExternalId" = r."FromExternalId"
                INNER JOIN "FranchiseMembers" b
                    ON b."FranchiseId" = @franchise
                   AND b."MediaType" = r."ToMediaType"
                   AND b."Provider" = r."ToProvider"
                   AND b."ExternalId" = r."ToExternalId"
                WHERE r."ReviewState" <> 'rejected'
                ORDER BY r."UpdatedAtUtc";
                """;
            Add(command, "@franchise", franchiseId.ToString("D"));

            var result = new List<MediaRelation>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (WatchlistMediaTypeNames.Parse(reader.GetString(0)) is not { } fromType ||
                    WatchlistMediaTypeNames.Parse(reader.GetString(3)) is not { } toType)
                {
                    continue;
                }

                result.Add(new MediaRelation(
                    new WatchlistIdentity(fromType, reader.GetString(1), reader.GetString(2)),
                    new WatchlistIdentity(toType, reader.GetString(4), reader.GetString(5)),
                    reader.GetString(6)));
            }

            return result;
        }, cancellationToken);

    /// <summary>Stored form of a relation type: lower case with dashes, for example "side-story".</summary>
    public static string NormalizeRelationType(string relationType)
    {
        var value = relationType.Trim().ToLowerInvariant().Replace('_', '-');
        if (string.IsNullOrWhiteSpace(value) || value.Length > 40)
        {
            throw new ArgumentException("Invalid relation type.", nameof(relationType));
        }

        return value;
    }

    private async Task<T> WithConnectionAsync<T>(
        Func<DbConnection, Task<T>> action,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            return await action(connection);
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    private Task WithConnectionAsync(
        Func<DbConnection, Task> action,
        CancellationToken cancellationToken) =>
        WithConnectionAsync<bool>(async connection =>
        {
            await action(connection);
            return true;
        }, cancellationToken);

    private static void BindIdentity(DbCommand command, string prefix, WatchlistIdentity identity)
    {
        Add(command, $"@{prefix}Type", WatchlistMediaTypeNames.ToStorage(identity.MediaType));
        Add(command, $"@{prefix}Provider", identity.ProviderKey);
        Add(command, $"@{prefix}Id", identity.ExternalKey);
    }

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static string NormalizeSource(string source)
    {
        var value = source.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 80)
        {
            throw new ArgumentException("Invalid relation source.", nameof(source));
        }

        return value;
    }
}
