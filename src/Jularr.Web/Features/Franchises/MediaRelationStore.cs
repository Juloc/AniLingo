using System.Data;
using System.Data.Common;
using System.Globalization;
using Jularr.Web.Data;
using Jularr.Web.Features.Watchlist;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Franchises;

public enum MediaRelationReviewState
{
    Confirmed,
    Pending,
    Rejected
}

public sealed record MediaRelation(
    Guid Id,
    WatchlistIdentity From,
    WatchlistIdentity To,
    string RelationType,
    string Source,
    double Confidence,
    MediaRelationReviewState ReviewState,
    bool IsManual,
    DateTime UpdatedAtUtc);

public sealed class MediaRelationStore(AppDbContext db)
{
    public Task UpsertProviderAsync(
        WatchlistIdentity from,
        WatchlistIdentity to,
        string relationType,
        string source,
        double confidence,
        bool confirmed,
        CancellationToken cancellationToken)
    {
        var normalizedType = NormalizeRelationType(relationType);
        var normalizedSource = NormalizeSource(source);
        var state = confirmed ? "confirmed" : "pending";
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
                     @source, @confidence, @reviewState, 0, @updated)
                ON CONFLICT (
                    "FromMediaType", "FromProvider", "FromExternalId",
                    "ToMediaType", "ToProvider", "ToExternalId", "RelationType"
                ) DO UPDATE SET
                    "Source" = CASE
                        WHEN "MediaRelations"."IsManual" = 1 THEN "MediaRelations"."Source"
                        ELSE excluded."Source"
                    END,
                    "Confidence" = CASE
                        WHEN "MediaRelations"."IsManual" = 1 THEN "MediaRelations"."Confidence"
                        ELSE excluded."Confidence"
                    END,
                    "ReviewState" = CASE
                        WHEN "MediaRelations"."IsManual" = 1 THEN "MediaRelations"."ReviewState"
                        WHEN "MediaRelations"."ReviewState" = 'rejected' THEN 'rejected'
                        ELSE excluded."ReviewState"
                    END,
                    "UpdatedAtUtc" = excluded."UpdatedAtUtc";
                """;
            BindIdentity(command, "from", from);
            BindIdentity(command, "to", to);
            Add(command, "@id", Guid.NewGuid().ToString("D"));
            Add(command, "@relationType", normalizedType);
            Add(command, "@source", normalizedSource);
            Add(command, "@confidence", Math.Clamp(confidence, 0, 1));
            Add(command, "@reviewState", state);
            Add(command, "@updated", now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    public Task UpsertManualAsync(
        WatchlistIdentity from,
        WatchlistIdentity to,
        string relationType,
        CancellationToken cancellationToken)
    {
        var normalizedType = NormalizeRelationType(relationType);
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
                     'manual', 1.0, 'confirmed', 1, @updated)
                ON CONFLICT (
                    "FromMediaType", "FromProvider", "FromExternalId",
                    "ToMediaType", "ToProvider", "ToExternalId", "RelationType"
                ) DO UPDATE SET
                    "Source" = 'manual',
                    "Confidence" = 1.0,
                    "ReviewState" = 'confirmed',
                    "IsManual" = 1,
                    "UpdatedAtUtc" = excluded."UpdatedAtUtc";
                """;
            BindIdentity(command, "from", from);
            BindIdentity(command, "to", to);
            Add(command, "@id", Guid.NewGuid().ToString("D"));
            Add(command, "@relationType", normalizedType);
            Add(command, "@updated", now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    public Task SetReviewStateAsync(
        Guid relationId,
        MediaRelationReviewState state,
        CancellationToken cancellationToken) =>
        WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE "MediaRelations"
                SET "ReviewState" = @state,
                    "IsManual" = 1,
                    "Source" = CASE WHEN @state = 'confirmed' THEN 'manual' ELSE "Source" END,
                    "UpdatedAtUtc" = @updated
                WHERE "Id" = @id;
                """;
            Add(command, "@state", ReviewStateName(state));
            Add(command, "@updated", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            Add(command, "@id", relationId.ToString("D"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);

    public Task DeleteManualAsync(Guid relationId, CancellationToken cancellationToken) =>
        WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                DELETE FROM "MediaRelations"
                WHERE "Id" = @id AND "IsManual" = 1;
                """;
            Add(command, "@id", relationId.ToString("D"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);

    public async Task<IReadOnlyList<MediaRelation>> GetForMembersAsync(
        IReadOnlyCollection<WatchlistIdentity> members,
        CancellationToken cancellationToken)
    {
        if (members.Count == 0)
        {
            return [];
        }

        var keys = members.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        return await WithConnectionAsync<IReadOnlyList<MediaRelation>>(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT "Id",
                       "FromMediaType", "FromProvider", "FromExternalId",
                       "ToMediaType", "ToProvider", "ToExternalId",
                       "RelationType", "Source", "Confidence", "ReviewState",
                       "IsManual", "UpdatedAtUtc"
                FROM "MediaRelations"
                WHERE "ReviewState" <> 'rejected'
                ORDER BY "RelationType", "UpdatedAtUtc" DESC;
                """;

            var result = new List<MediaRelation>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var relation = Read(reader);
                if (keys.Contains(relation.From.Key) && keys.Contains(relation.To.Key))
                {
                    result.Add(relation);
                }
            }

            return result;
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<MediaRelation>> GetPendingAsync(CancellationToken cancellationToken) =>
        await WithConnectionAsync<IReadOnlyList<MediaRelation>>(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT "Id",
                       "FromMediaType", "FromProvider", "FromExternalId",
                       "ToMediaType", "ToProvider", "ToExternalId",
                       "RelationType", "Source", "Confidence", "ReviewState",
                       "IsManual", "UpdatedAtUtc"
                FROM "MediaRelations"
                WHERE "ReviewState" = 'pending'
                ORDER BY "Confidence" DESC, "UpdatedAtUtc" DESC;
                """;

            var result = new List<MediaRelation>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(Read(reader));
            }

            return result;
        }, cancellationToken);

    private static MediaRelation Read(DbDataReader reader)
    {
        var fromType = WatchlistMediaTypeNames.Parse(reader.GetString(1))
            ?? throw new InvalidOperationException("Unknown relation media type.");
        var toType = WatchlistMediaTypeNames.Parse(reader.GetString(4))
            ?? throw new InvalidOperationException("Unknown relation media type.");

        return new MediaRelation(
            Guid.Parse(reader.GetString(0)),
            new WatchlistIdentity(fromType, reader.GetString(2), reader.GetString(3)),
            new WatchlistIdentity(toType, reader.GetString(5), reader.GetString(6)),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetDouble(9),
            ParseReviewState(reader.GetString(10)),
            reader.GetInt32(11) != 0,
            DateTime.Parse(reader.GetString(12), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
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

    private static string NormalizeRelationType(string relationType)
    {
        var value = relationType.Trim().ToLowerInvariant().Replace('_', '-');
        if (string.IsNullOrWhiteSpace(value) || value.Length > 40)
        {
            throw new ArgumentException("Invalid relation type.", nameof(relationType));
        }

        return value;
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

    private static string ReviewStateName(MediaRelationReviewState state) => state switch
    {
        MediaRelationReviewState.Confirmed => "confirmed",
        MediaRelationReviewState.Pending => "pending",
        MediaRelationReviewState.Rejected => "rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    private static MediaRelationReviewState ParseReviewState(string value) => value switch
    {
        "confirmed" => MediaRelationReviewState.Confirmed,
        "pending" => MediaRelationReviewState.Pending,
        "rejected" => MediaRelationReviewState.Rejected,
        _ => throw new InvalidOperationException("Unknown relation review state.")
    };
}
