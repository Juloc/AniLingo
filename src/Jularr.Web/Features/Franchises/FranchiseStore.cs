using System.Data;
using System.Data.Common;
using System.Globalization;
using Jularr.Web.Data;
using Jularr.Web.Features.Watchlist;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Franchises;

public sealed class FranchiseStore(AppDbContext db)
{
    public async Task<Guid> GetOrCreateBySeedAsync(WatchlistDraft seed, CancellationToken cancellationToken)
    {
        var existing = await FindBySeedAsync(seed.Identity, cancellationToken);
        if (existing is { } id)
        {
            await UpsertMemberAsync(id, seed, null, true, cancellationToken);
            return id;
        }

        var franchiseId = Guid.NewGuid();
        await WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO "Franchises"
                    ("Id", "Title", "SeedMediaType", "SeedProvider", "SeedExternalId", "CreatedAtUtc", "UpdatedAtUtc")
                VALUES
                    (@id, @title, @mediaType, @provider, @externalId, @created, @updated);
                """;
            var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            Add(command, "@id", franchiseId.ToString("D"));
            Add(command, "@title", seed.Title.Trim());
            Add(command, "@mediaType", WatchlistMediaTypeNames.ToStorage(seed.Identity.MediaType));
            Add(command, "@provider", seed.Identity.ProviderKey);
            Add(command, "@externalId", seed.Identity.ExternalKey);
            Add(command, "@created", now);
            Add(command, "@updated", now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);

        await UpsertMemberAsync(franchiseId, seed, null, true, cancellationToken);
        return franchiseId;
    }

    public Task FollowAsync(string profileId, Guid franchiseId, CancellationToken cancellationToken) =>
        WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO "ProfileFranchiseFollows" ("ProfileId", "FranchiseId", "FollowedAtUtc")
                VALUES (@profile, @franchise, @followed)
                ON CONFLICT ("ProfileId", "FranchiseId") DO NOTHING;
                """;
            Add(command, "@profile", profileId);
            Add(command, "@franchise", franchiseId.ToString("D"));
            Add(command, "@followed", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);

    public Task UnfollowAsync(string profileId, Guid franchiseId, CancellationToken cancellationToken) =>
        WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                DELETE FROM "ProfileFranchiseFollows"
                WHERE "ProfileId" = @profile AND "FranchiseId" = @franchise;
                """;
            Add(command, "@profile", profileId);
            Add(command, "@franchise", franchiseId.ToString("D"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);

    public Task UpsertMemberAsync(
        Guid franchiseId,
        WatchlistDraft media,
        string? relationType,
        bool isSeed,
        CancellationToken cancellationToken) =>
        WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO "FranchiseMembers"
                    ("FranchiseId", "MediaType", "Provider", "ExternalId", "Title", "NativeTitle",
                     "CoverImageUrl", "Format", "Status", "Year", "LocalMediaId", "DetailsUrl",
                     "RelationType", "IsSeed", "UpdatedAtUtc")
                VALUES
                    (@franchise, @mediaType, @provider, @externalId, @title, @nativeTitle,
                     @cover, @format, @status, @year, @localMediaId, @detailsUrl,
                     @relationType, @isSeed, @updated)
                ON CONFLICT ("FranchiseId", "MediaType", "Provider", "ExternalId") DO UPDATE SET
                    "Title" = excluded."Title",
                    "NativeTitle" = excluded."NativeTitle",
                    "CoverImageUrl" = excluded."CoverImageUrl",
                    "Format" = excluded."Format",
                    "Status" = excluded."Status",
                    "Year" = excluded."Year",
                    "LocalMediaId" = COALESCE(excluded."LocalMediaId", "FranchiseMembers"."LocalMediaId"),
                    "DetailsUrl" = COALESCE(excluded."DetailsUrl", "FranchiseMembers"."DetailsUrl"),
                    "RelationType" = COALESCE(excluded."RelationType", "FranchiseMembers"."RelationType"),
                    "IsSeed" = CASE WHEN excluded."IsSeed" = 1 THEN 1 ELSE "FranchiseMembers"."IsSeed" END,
                    "UpdatedAtUtc" = excluded."UpdatedAtUtc";
                """;
            Add(command, "@franchise", franchiseId.ToString("D"));
            Add(command, "@mediaType", WatchlistMediaTypeNames.ToStorage(media.Identity.MediaType));
            Add(command, "@provider", media.Identity.ProviderKey);
            Add(command, "@externalId", media.Identity.ExternalKey);
            Add(command, "@title", media.Title.Trim());
            Add(command, "@nativeTitle", Clean(media.NativeTitle));
            Add(command, "@cover", Clean(media.CoverImageUrl));
            Add(command, "@format", Clean(media.Format));
            Add(command, "@status", Clean(media.Status));
            Add(command, "@year", media.Year);
            Add(command, "@localMediaId", media.LocalMediaId?.ToString("D"));
            Add(command, "@detailsUrl", Clean(media.DetailsUrl));
            Add(command, "@relationType", Clean(relationType));
            Add(command, "@isSeed", isSeed ? 1 : 0);
            Add(command, "@updated", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);

    public async Task<IReadOnlyList<FranchiseMember>> GetMembersAsync(Guid franchiseId, CancellationToken cancellationToken) =>
        await WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT "MediaType", "Provider", "ExternalId", "Title", "NativeTitle",
                       "CoverImageUrl", "Format", "Status", "Year", "LocalMediaId",
                       "DetailsUrl", "RelationType", "IsSeed"
                FROM "FranchiseMembers"
                WHERE "FranchiseId" = @franchise
                ORDER BY "IsSeed" DESC, "Title";
                """;
            Add(command, "@franchise", franchiseId.ToString("D"));
            var result = new List<FranchiseMember>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var type = WatchlistMediaTypeNames.Parse(reader.GetString(0));
                if (type is null) continue;

                Guid? localId = reader.IsDBNull(9) || !Guid.TryParse(reader.GetString(9), out var parsed)
                    ? null
                    : parsed;
                var draft = new WatchlistDraft(
                    new WatchlistIdentity(type.Value, reader.GetString(1), reader.GetString(2)),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    localId,
                    reader.IsDBNull(10) ? null : reader.GetString(10));
                result.Add(new FranchiseMember(
                    franchiseId,
                    draft,
                    reader.IsDBNull(11) ? null : reader.GetString(11),
                    reader.GetInt32(12) != 0));
            }

            return result.ToArray();
        }, cancellationToken);

    public async Task<IReadOnlyList<FranchiseSummary>> ListFollowedAsync(string profileId, CancellationToken cancellationToken) =>
        await WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT f."Id", f."Title", f."SeedMediaType", f."SeedProvider", f."SeedExternalId",
                       f."LastRefreshedAtUtc",
                       (SELECT COUNT(*) FROM "FranchiseMembers" m WHERE m."FranchiseId" = f."Id")
                FROM "Franchises" f
                INNER JOIN "ProfileFranchiseFollows" pf ON pf."FranchiseId" = f."Id"
                WHERE pf."ProfileId" = @profile
                ORDER BY f."Title";
                """;
            Add(command, "@profile", profileId);
            var result = new List<FranchiseSummary>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var mediaType = WatchlistMediaTypeNames.Parse(reader.GetString(2));
                if (mediaType is null || !Guid.TryParse(reader.GetString(0), out var id)) continue;

                DateTime? refreshed = null;
                if (!reader.IsDBNull(5) &&
                    DateTime.TryParse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                {
                    refreshed = parsed;
                }

                result.Add(new FranchiseSummary(
                    id,
                    reader.GetString(1),
                    new WatchlistIdentity(mediaType.Value, reader.GetString(3), reader.GetString(4)),
                    refreshed,
                    Convert.ToInt32(reader.GetValue(6), CultureInfo.InvariantCulture)));
            }

            return result.ToArray();
        }, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListFollowedFranchiseIdsAsync(CancellationToken cancellationToken) =>
        await WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """SELECT DISTINCT "FranchiseId" FROM "ProfileFranchiseFollows";""";
            var result = new List<Guid>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (Guid.TryParse(reader.GetString(0), out var id)) result.Add(id);
            }

            return result.ToArray();
        }, cancellationToken);

    public Task MarkRefreshedAsync(Guid franchiseId, CancellationToken cancellationToken) =>
        WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE "Franchises"
                SET "LastRefreshedAtUtc" = @refreshed, "UpdatedAtUtc" = @refreshed
                WHERE "Id" = @id;
                """;
            Add(command, "@id", franchiseId.ToString("D"));
            Add(command, "@refreshed", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);

    private async Task<Guid?> FindBySeedAsync(WatchlistIdentity seed, CancellationToken cancellationToken) =>
        await WithConnectionAsync<Guid?>(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT "Id" FROM "Franchises"
                WHERE "SeedMediaType" = @mediaType
                  AND "SeedProvider" = @provider
                  AND "SeedExternalId" = @externalId
                LIMIT 1;
                """;
            Add(command, "@mediaType", WatchlistMediaTypeNames.ToStorage(seed.MediaType));
            Add(command, "@provider", seed.ProviderKey);
            Add(command, "@externalId", seed.ExternalKey);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return value is string text && Guid.TryParse(text, out var id) ? id : null;
        }, cancellationToken);

    private async Task<T> WithConnectionAsync<T>(
        Func<DbConnection, Task<T>> action,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(cancellationToken);
        try
        {
            return await action(connection);
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

    private Task WithConnectionAsync(Func<DbConnection, Task> action, CancellationToken cancellationToken) =>
        WithConnectionAsync<bool>(async connection =>
        {
            await action(connection);
            return true;
        }, cancellationToken);

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
