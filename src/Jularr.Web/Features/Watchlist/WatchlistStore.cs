using System.Data;
using System.Data.Common;
using System.Globalization;
using Jularr.Web.Data;

namespace Jularr.Web.Features.Watchlist;

/// <summary>
/// Profile-scoped local follow state. Provider accounts are never the source of truth: provider
/// identity only identifies a work so metadata/release adapters can enrich it.
/// </summary>
public sealed class WatchlistStore(AppDbContext db)
{
    public async Task FollowAsync(string profileId, WatchlistDraft draft, CancellationToken cancellationToken)
    {
        Validate(profileId, draft);
        await UpsertPreferenceAsync(profileId, draft, WatchPreferenceState.Follow, cancellationToken);
    }

    public async Task UnfollowAsync(string profileId, WatchlistIdentity identity, CancellationToken cancellationToken)
    {
        ValidateProfile(profileId);
        if (await IsIncludedByFollowedFranchiseAsync(profileId, identity, cancellationToken))
        {
            var existing = (await GetEffectiveAsync(profileId, cancellationToken))
                .FirstOrDefault(item => item.Identity.Key == identity.Key);
            var draft = existing is null
                ? new WatchlistDraft(identity, identity.ExternalKey)
                : new WatchlistDraft(
                    identity,
                    existing.Title,
                    existing.NativeTitle,
                    existing.CoverImageUrl,
                    existing.Format,
                    existing.Status,
                    existing.Year,
                    existing.LocalMediaId,
                    existing.DetailsUrl);
            await UpsertPreferenceAsync(profileId, draft, WatchPreferenceState.Ignore, cancellationToken);
            return;
        }

        await WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                DELETE FROM "ProfileWatchlistPreferences"
                WHERE "ProfileId" = @profile
                  AND "MediaType" = @mediaType
                  AND "Provider" = @provider
                  AND "ExternalId" = @externalId;
                """;
            Add(command, "@profile", profileId);
            Add(command, "@mediaType", WatchlistMediaTypeNames.ToStorage(identity.MediaType));
            Add(command, "@provider", identity.ProviderKey);
            Add(command, "@externalId", identity.ExternalKey);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<WatchlistItem>> GetEffectiveAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        ValidateProfile(profileId);
        return await WithConnectionAsync(async connection =>
        {
            var items = new Dictionary<string, WatchlistItem>(StringComparer.Ordinal);

            await using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    """
                    SELECT m."MediaType", m."Provider", m."ExternalId", m."Title", m."NativeTitle",
                           m."CoverImageUrl", m."Format", m."Status", m."Year", m."LocalMediaId",
                           m."DetailsUrl", f."Id", f."Title"
                    FROM "FranchiseMembers" m
                    INNER JOIN "ProfileFranchiseFollows" pf ON pf."FranchiseId" = m."FranchiseId"
                    INNER JOIN "Franchises" f ON f."Id" = m."FranchiseId"
                    WHERE pf."ProfileId" = @profile
                    ORDER BY f."Title", m."Title";
                    """;
                Add(command, "@profile", profileId);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var item = ReadItem(reader, isExplicit: false);
                    items[item.Identity.Key] = item;
                }
            }

            await using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    """
                    SELECT "MediaType", "Provider", "ExternalId", "Title", "NativeTitle",
                           "CoverImageUrl", "Format", "Status", "Year", "LocalMediaId",
                           "DetailsUrl", NULL, NULL
                    FROM "ProfileWatchlistPreferences"
                    WHERE "ProfileId" = @profile AND "FollowState" = 'follow'
                    ORDER BY "UpdatedAtUtc" DESC;
                    """;
                Add(command, "@profile", profileId);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var item = ReadItem(reader, isExplicit: true);
                    items[item.Identity.Key] = item;
                }
            }

            await using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    """
                    SELECT "MediaType", "Provider", "ExternalId"
                    FROM "ProfileWatchlistPreferences"
                    WHERE "ProfileId" = @profile AND "FollowState" = 'ignore';
                    """;
                Add(command, "@profile", profileId);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var type = WatchlistMediaTypeNames.Parse(reader.GetString(0));
                    if (type is null)
                    {
                        continue;
                    }

                    var identity = new WatchlistIdentity(type.Value, reader.GetString(1), reader.GetString(2));
                    items.Remove(identity.Key);
                }
            }

            return items.Values
                .OrderBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<WatchlistItem>> GetEffectiveAcrossProfilesAsync(
        CancellationToken cancellationToken)
    {
        var profiles = await WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT DISTINCT "ProfileId" FROM "ProfileWatchlistPreferences"
                UNION
                SELECT DISTINCT "ProfileId" FROM "ProfileFranchiseFollows";
                """;
            var result = new List<string>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(reader.GetString(0));
            }

            return result.ToArray();
        }, cancellationToken);

        var items = new Dictionary<string, WatchlistItem>(StringComparer.Ordinal);
        foreach (var profile in profiles)
        {
            foreach (var item in await GetEffectiveAsync(profile, cancellationToken))
            {
                items[item.Identity.Key] = item;
            }
        }

        return items.Values.ToArray();
    }

    public async Task<HashSet<string>> GetEffectiveKeysAsync(
        string profileId,
        CancellationToken cancellationToken) =>
        (await GetEffectiveAsync(profileId, cancellationToken))
            .Select(item => item.Identity.Key)
            .ToHashSet(StringComparer.Ordinal);

    private async Task UpsertPreferenceAsync(
        string profileId,
        WatchlistDraft draft,
        WatchPreferenceState state,
        CancellationToken cancellationToken)
    {
        await WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO "ProfileWatchlistPreferences"
                    ("ProfileId", "MediaType", "Provider", "ExternalId", "FollowState", "Title",
                     "NativeTitle", "CoverImageUrl", "Format", "Status", "Year", "LocalMediaId",
                     "DetailsUrl", "UpdatedAtUtc")
                VALUES
                    (@profile, @mediaType, @provider, @externalId, @state, @title,
                     @nativeTitle, @cover, @format, @status, @year, @localMediaId,
                     @detailsUrl, @updated)
                ON CONFLICT ("ProfileId", "MediaType", "Provider", "ExternalId") DO UPDATE SET
                    "FollowState" = excluded."FollowState",
                    "Title" = excluded."Title",
                    "NativeTitle" = excluded."NativeTitle",
                    "CoverImageUrl" = excluded."CoverImageUrl",
                    "Format" = excluded."Format",
                    "Status" = excluded."Status",
                    "Year" = excluded."Year",
                    "LocalMediaId" = COALESCE(excluded."LocalMediaId", "ProfileWatchlistPreferences"."LocalMediaId"),
                    "DetailsUrl" = COALESCE(excluded."DetailsUrl", "ProfileWatchlistPreferences"."DetailsUrl"),
                    "UpdatedAtUtc" = excluded."UpdatedAtUtc";
                """;
            Add(command, "@profile", profileId);
            Add(command, "@mediaType", WatchlistMediaTypeNames.ToStorage(draft.Identity.MediaType));
            Add(command, "@provider", draft.Identity.ProviderKey);
            Add(command, "@externalId", draft.Identity.ExternalKey);
            Add(command, "@state", state == WatchPreferenceState.Follow ? "follow" : "ignore");
            Add(command, "@title", draft.Title.Trim());
            Add(command, "@nativeTitle", Clean(draft.NativeTitle));
            Add(command, "@cover", Clean(draft.CoverImageUrl));
            Add(command, "@format", Clean(draft.Format));
            Add(command, "@status", Clean(draft.Status));
            Add(command, "@year", draft.Year);
            Add(command, "@localMediaId", draft.LocalMediaId?.ToString("D"));
            Add(command, "@detailsUrl", Clean(draft.DetailsUrl));
            Add(command, "@updated", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    private async Task<bool> IsIncludedByFollowedFranchiseAsync(
        string profileId,
        WatchlistIdentity identity,
        CancellationToken cancellationToken) =>
        await WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT 1
                FROM "FranchiseMembers" m
                INNER JOIN "ProfileFranchiseFollows" pf ON pf."FranchiseId" = m."FranchiseId"
                WHERE pf."ProfileId" = @profile
                  AND m."MediaType" = @mediaType
                  AND m."Provider" = @provider
                  AND m."ExternalId" = @externalId
                LIMIT 1;
                """;
            Add(command, "@profile", profileId);
            Add(command, "@mediaType", WatchlistMediaTypeNames.ToStorage(identity.MediaType));
            Add(command, "@provider", identity.ProviderKey);
            Add(command, "@externalId", identity.ExternalKey);
            return await command.ExecuteScalarAsync(cancellationToken) is not null;
        }, cancellationToken);

    private static WatchlistItem ReadItem(DbDataReader reader, bool isExplicit)
    {
        var type = WatchlistMediaTypeNames.Parse(reader.GetString(0))
            ?? throw new InvalidOperationException("Unknown watchlist media type.");
        var identity = new WatchlistIdentity(type, reader.GetString(1), reader.GetString(2));
        var localMediaId = reader.IsDBNull(9) || !Guid.TryParse(reader.GetString(9), out var parsedLocal)
            ? null
            : parsedLocal;
        var franchiseId = reader.IsDBNull(11) || !Guid.TryParse(reader.GetString(11), out var parsedFranchise)
            ? null
            : parsedFranchise;

        return new WatchlistItem(
            identity,
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetInt32(8),
            localMediaId,
            reader.IsDBNull(10) ? null : reader.GetString(10),
            franchiseId,
            reader.IsDBNull(12) ? null : reader.GetString(12),
            isExplicit);
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
        WithConnectionAsync(async connection =>
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

    private static void Validate(string profileId, WatchlistDraft draft)
    {
        ValidateProfile(profileId);
        if (string.IsNullOrWhiteSpace(draft.Title) ||
            string.IsNullOrWhiteSpace(draft.Identity.Provider) ||
            string.IsNullOrWhiteSpace(draft.Identity.ExternalId))
        {
            throw new ArgumentException("A watchlist target needs provider identity and title.", nameof(draft));
        }
    }

    private static void ValidateProfile(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId) || profileId.Length > 80)
        {
            throw new ArgumentException("Invalid profile id.", nameof(profileId));
        }
    }
}
