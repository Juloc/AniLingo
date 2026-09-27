using System.Data;
using System.Data.Common;
using System.Globalization;
using Jularr.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Artwork;

public static class MediaArtworkScopes
{
    public const string AnimeSeries = "anime-series";
}

/// <summary>Where a Jularr-written artwork file came from; a higher rank may replace a lower one.</summary>
public static class MediaArtworkSources
{
    public const string Sonarr = "sonarr";
    public const string Migrated = "migrated";
    public const string AniList = "anilist";

    public static int Rank(string source) =>
        source switch
        {
            Sonarr => 3,
            Migrated => 2,
            AniList => 1,
            _ => 0
        };
}

/// <summary>An artwork file Jularr wrote beside the media, identified by size and last write.</summary>
public sealed record MediaArtworkAsset(
    string Scope,
    Guid OwnerId,
    int SeasonNumber,
    string Kind,
    string FileName,
    string Source,
    string? SourceIdentity,
    long FileLength,
    DateTime FileLastWriteTimeUtc)
{
    // SMB/NFS mounts may round timestamps; a user's replacement changes size or time far more.
    private static readonly TimeSpan TimestampTolerance = TimeSpan.FromSeconds(2);

    public bool Matches(FileInfo file) =>
        file.Exists &&
        file.Length == FileLength &&
        (file.LastWriteTimeUtc - FileLastWriteTimeUtc).Duration() <= TimestampTolerance;
}

/// <summary>Persistence of <see cref="MediaArtworkAsset"/> rows (table from migration 20260927180000).</summary>
public sealed class MediaArtworkAssetStore(AppDbContext db)
{
    public Task<IReadOnlyList<MediaArtworkAsset>> ListAsync(
        string scope,
        Guid ownerId,
        CancellationToken cancellationToken) =>
        WithConnectionAsync<IReadOnlyList<MediaArtworkAsset>>(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT "Scope", "OwnerId", "SeasonNumber", "Kind", "FileName", "Source",
                       "SourceIdentity", "FileLength", "FileLastWriteTimeUtc"
                FROM "MediaArtworkAssets"
                WHERE "Scope" = $scope AND "OwnerId" = $owner;
                """;
            Add(command, "$scope", scope);
            Add(command, "$owner", ownerId.ToString("D"));

            var rows = new List<MediaArtworkAsset>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new MediaArtworkAsset(
                    reader.GetString(0),
                    Guid.Parse(reader.GetString(1)),
                    reader.GetInt32(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.GetInt64(7),
                    DateTime.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
            }

            return rows;
        }, cancellationToken);

    public Task UpsertAsync(MediaArtworkAsset asset, CancellationToken cancellationToken) =>
        WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO "MediaArtworkAssets" (
                    "Scope", "OwnerId", "SeasonNumber", "Kind", "FileName", "Source",
                    "SourceIdentity", "FileLength", "FileLastWriteTimeUtc", "UpdatedAt")
                VALUES ($scope, $owner, $season, $kind, $file, $source, $identity, $length, $written, $now)
                ON CONFLICT("Scope", "OwnerId", "SeasonNumber", "Kind") DO UPDATE SET
                    "FileName" = excluded."FileName",
                    "Source" = excluded."Source",
                    "SourceIdentity" = excluded."SourceIdentity",
                    "FileLength" = excluded."FileLength",
                    "FileLastWriteTimeUtc" = excluded."FileLastWriteTimeUtc",
                    "UpdatedAt" = excluded."UpdatedAt";
                """;
            Add(command, "$scope", asset.Scope);
            Add(command, "$owner", asset.OwnerId.ToString("D"));
            Add(command, "$season", asset.SeasonNumber);
            Add(command, "$kind", asset.Kind);
            Add(command, "$file", asset.FileName);
            Add(command, "$source", asset.Source);
            Add(command, "$identity", asset.SourceIdentity);
            Add(command, "$length", asset.FileLength);
            Add(command, "$written", asset.FileLastWriteTimeUtc);
            Add(command, "$now", DateTime.UtcNow);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }, cancellationToken);

    private async Task<T> WithConnectionAsync<T>(Func<DbConnection, Task<T>> action, CancellationToken cancellationToken)
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

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value switch
        {
            null => DBNull.Value,
            DateTime timestamp => timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            _ => value
        };
        command.Parameters.Add(parameter);
    }
}
