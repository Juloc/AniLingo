using System.Data;
using System.Data.Common;
using System.Globalization;
using Jularr.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.ChapterArtwork;

/// <summary>Persistence of chapter artwork metadata and settings (tables from migration 20260928120000).</summary>
public sealed class ChapterArtworkStore(AppDbContext db)
{
    private const string Columns =
        """
        "Id", "WorkId", "ChapterId", "ChapterNumber", "Status", "AssetPath", "MediaType", "ContentHash",
        "ByteSize", "ProviderId", "Model", "PromptVersion", "ContextHash", "ContextScope", "Prompt",
        "NeutralPrompt", "Style", "Quality", "Error", "OperationId", "RequestedByProfileId",
        "CreatedAt", "UpdatedAt", "AcceptedAt"
        """;

    public Task InsertAsync(ChapterArtworkItem item, CancellationToken cancellationToken) =>
        ExecuteAsync(
            $"""
            INSERT INTO "ChapterArtworks" ({Columns})
            VALUES (@id, @work, @chapter, @number, @status, @asset, @media, @hash, @size, @provider, @model,
                    @promptVersion, @contextHash, @scope, @prompt, @neutral, @style, @quality, @error,
                    @operation, @profile, @created, @updated, @accepted);
            """,
            command => Bind(command, item),
            cancellationToken);

    public Task UpdateAsync(ChapterArtworkItem item, CancellationToken cancellationToken) =>
        ExecuteAsync(
            """
            UPDATE "ChapterArtworks" SET
                "ChapterId" = @chapter, "Status" = @status, "AssetPath" = @asset, "MediaType" = @media,
                "ContentHash" = @hash, "ByteSize" = @size, "ProviderId" = @provider, "Model" = @model,
                "PromptVersion" = @promptVersion, "ContextHash" = @contextHash, "ContextScope" = @scope,
                "Prompt" = @prompt, "NeutralPrompt" = @neutral, "Style" = @style, "Quality" = @quality,
                "Error" = @error, "OperationId" = @operation, "UpdatedAt" = @updated, "AcceptedAt" = @accepted
            WHERE "Id" = @id;
            """,
            command => Bind(command, item),
            cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(
            """DELETE FROM "ChapterArtworks" WHERE "Id" = @id;""",
            command => Add(command, "@id", Key(id)),
            cancellationToken);

    public Task DeleteWorkAsync(Guid workId, CancellationToken cancellationToken) =>
        ExecuteAsync(
            """
            DELETE FROM "ChapterArtworks" WHERE "WorkId" = @work;
            DELETE FROM "ChapterArtworkWorkSettings" WHERE "WorkId" = @work;
            """,
            command => Add(command, "@work", Key(workId)),
            cancellationToken);

    public Task<ChapterArtworkItem?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        QuerySingleAsync(
            $"""SELECT {Columns} FROM "ChapterArtworks" WHERE "Id" = @id LIMIT 1;""",
            command => Add(command, "@id", Key(id)),
            cancellationToken);

    public Task<ChapterArtworkItem?> GetAcceptedAsync(
        Guid workId,
        int chapterNumber,
        CancellationToken cancellationToken) =>
        QuerySingleAsync(
            $"""
            SELECT {Columns} FROM "ChapterArtworks"
            WHERE "WorkId" = @work AND "ChapterNumber" = @number AND "Status" = 'accepted'
            LIMIT 1;
            """,
            command =>
            {
                Add(command, "@work", Key(workId));
                Add(command, "@number", chapterNumber);
            },
            cancellationToken);

    public Task<IReadOnlyList<ChapterArtworkItem>> ListChapterAsync(
        Guid workId,
        int chapterNumber,
        CancellationToken cancellationToken) =>
        QueryAsync(
            $"""
            SELECT {Columns} FROM "ChapterArtworks"
            WHERE "WorkId" = @work AND "ChapterNumber" = @number
            ORDER BY "CreatedAt";
            """,
            command =>
            {
                Add(command, "@work", Key(workId));
                Add(command, "@number", chapterNumber);
            },
            cancellationToken);

    public Task<IReadOnlyList<ChapterArtworkItem>> ListWorkAsync(
        Guid workId,
        CancellationToken cancellationToken) =>
        QueryAsync(
            $"""
            SELECT {Columns} FROM "ChapterArtworks"
            WHERE "WorkId" = @work
            ORDER BY "ChapterNumber", "CreatedAt";
            """,
            command => Add(command, "@work", Key(workId)),
            cancellationToken);

    /// <summary>Marks rows left queued or generating by a previous process as failed so they can be retried.</summary>
    public Task<int> FailInterruptedAsync(DateTime before, CancellationToken cancellationToken) =>
        WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE "ChapterArtworks" SET "Status" = 'failed', "Error" = 'Interrupted before completion.', "UpdatedAt" = @now
                WHERE "Status" IN ('queued', 'generating') AND "UpdatedAt" < @before;
                """;
            Add(command, "@now", DateTime.UtcNow);
            Add(command, "@before", before);
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);

    public async Task<ChapterArtworkPreferences> GetPreferencesAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        var stored = await WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT "Enabled", "AutoGenerate", "Style", "Quality", "Variations"
                FROM "ChapterArtworkPreferences" WHERE "ProfileId" = @profile LIMIT 1;
                """;
            Add(command, "@profile", profileId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken)
                ? new ChapterArtworkPreferences(
                    reader.GetInt64(0) != 0,
                    reader.GetInt64(1) != 0,
                    ChapterArtworkNames.ParseStyle(reader.GetString(2)),
                    ChapterArtworkNames.ParseQuality(reader.GetString(3)),
                    (int)reader.GetInt64(4))
                : null;
        }, cancellationToken);

        return stored ?? ChapterArtworkPreferences.Default;
    }

    public Task SavePreferencesAsync(
        string profileId,
        ChapterArtworkPreferences preferences,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            """
            INSERT INTO "ChapterArtworkPreferences" ("ProfileId", "Enabled", "AutoGenerate", "Style", "Quality", "Variations", "UpdatedAt")
            VALUES (@profile, @enabled, @auto, @style, @quality, @variations, @now)
            ON CONFLICT("ProfileId") DO UPDATE SET
                "Enabled" = excluded."Enabled",
                "AutoGenerate" = excluded."AutoGenerate",
                "Style" = excluded."Style",
                "Quality" = excluded."Quality",
                "Variations" = excluded."Variations",
                "UpdatedAt" = excluded."UpdatedAt";
            """,
            command =>
            {
                Add(command, "@profile", profileId);
                Add(command, "@enabled", preferences.Enabled ? 1 : 0);
                Add(command, "@auto", preferences.AutoGenerate ? 1 : 0);
                Add(command, "@style", ChapterArtworkNames.Style(preferences.Style));
                Add(command, "@quality", ChapterArtworkNames.Quality(preferences.Quality));
                Add(command, "@variations", Math.Clamp(preferences.Variations, 1, ChapterArtworkPreferences.MaxVariations));
                Add(command, "@now", DateTime.UtcNow);
            },
            cancellationToken);

    /// <summary>Profiles that asked for automatic generation of missing artwork.</summary>
    public Task<IReadOnlyList<string>> ListAutoGenerateProfilesAsync(CancellationToken cancellationToken) =>
        WithConnectionAsync<IReadOnlyList<string>>(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """SELECT "ProfileId" FROM "ChapterArtworkPreferences" WHERE "Enabled" = 1 AND "AutoGenerate" = 1;""";
            var profiles = new List<string>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                profiles.Add(reader.GetString(0));
            }

            return profiles;
        }, cancellationToken);

    public async Task<ChapterArtworkWorkSettings> GetWorkSettingsAsync(
        Guid workId,
        CancellationToken cancellationToken)
    {
        var stored = await WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT "Enabled", "Style", "SeriesStyle", "UseChapterTitles"
                FROM "ChapterArtworkWorkSettings" WHERE "WorkId" = @work LIMIT 1;
                """;
            Add(command, "@work", Key(workId));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken)
                ? new ChapterArtworkWorkSettings(
                    workId,
                    reader.IsDBNull(0) ? null : reader.GetInt64(0) != 0,
                    reader.IsDBNull(1) ? null : ChapterArtworkNames.ParseStyle(reader.GetString(1)),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetInt64(3) != 0)
                : null;
        }, cancellationToken);

        return stored ?? ChapterArtworkWorkSettings.Default(workId);
    }

    public Task SaveWorkSettingsAsync(
        ChapterArtworkWorkSettings settings,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            """
            INSERT INTO "ChapterArtworkWorkSettings" ("WorkId", "Enabled", "Style", "SeriesStyle", "UseChapterTitles", "UpdatedAt")
            VALUES (@work, @enabled, @style, @series, @titles, @now)
            ON CONFLICT("WorkId") DO UPDATE SET
                "Enabled" = excluded."Enabled",
                "Style" = excluded."Style",
                "SeriesStyle" = excluded."SeriesStyle",
                "UseChapterTitles" = excluded."UseChapterTitles",
                "UpdatedAt" = excluded."UpdatedAt";
            """,
            command =>
            {
                Add(command, "@work", Key(settings.WorkId));
                Add(command, "@enabled", settings.Enabled is bool enabled ? (enabled ? 1 : 0) : null);
                Add(command, "@style", settings.Style is { } style ? ChapterArtworkNames.Style(style) : null);
                Add(command, "@series", Clean(settings.SeriesStyle, ChapterArtworkWorkSettings.SeriesStyleLength));
                Add(command, "@titles", settings.UseChapterTitles ? 1 : 0);
                Add(command, "@now", DateTime.UtcNow);
            },
            cancellationToken);

    private static void Bind(DbCommand command, ChapterArtworkItem item)
    {
        Add(command, "@id", Key(item.Id));
        Add(command, "@work", Key(item.WorkId));
        Add(command, "@chapter", Key(item.ChapterId));
        Add(command, "@number", item.ChapterNumber);
        Add(command, "@status", ChapterArtworkNames.Status(item.Status));
        Add(command, "@asset", item.AssetPath);
        Add(command, "@media", item.MediaType);
        Add(command, "@hash", item.ContentHash);
        Add(command, "@size", item.ByteSize);
        Add(command, "@provider", item.ProviderId);
        Add(command, "@model", item.Model);
        Add(command, "@promptVersion", item.PromptVersion);
        Add(command, "@contextHash", item.ContextHash);
        Add(command, "@scope", item.ContextScope);
        Add(command, "@prompt", item.Prompt);
        Add(command, "@neutral", item.NeutralPrompt ? 1 : 0);
        Add(command, "@style", ChapterArtworkNames.Style(item.Style));
        Add(command, "@quality", ChapterArtworkNames.Quality(item.Quality));
        Add(command, "@error", Clean(item.Error, 1000));
        Add(command, "@operation", item.OperationId is Guid operation ? Key(operation) : null);
        Add(command, "@profile", item.RequestedByProfileId);
        Add(command, "@created", item.CreatedAt);
        Add(command, "@updated", item.UpdatedAt);
        Add(command, "@accepted", item.AcceptedAt);
    }

    private static ChapterArtworkItem Read(DbDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            Guid.Parse(reader.GetString(2)),
            (int)reader.GetInt64(3),
            ChapterArtworkNames.ParseStatus(reader.GetString(4)),
            NullableString(reader, 5),
            NullableString(reader, 6),
            NullableString(reader, 7),
            reader.IsDBNull(8) ? null : reader.GetInt64(8),
            NullableString(reader, 9),
            NullableString(reader, 10),
            (int)reader.GetInt64(11),
            NullableString(reader, 12),
            reader.GetString(13),
            NullableString(reader, 14),
            reader.GetInt64(15) != 0,
            ChapterArtworkNames.ParseStyle(reader.GetString(16)),
            ChapterArtworkNames.ParseQuality(reader.GetString(17)),
            NullableString(reader, 18),
            NullableString(reader, 19) is string operation ? Guid.Parse(operation) : null,
            reader.GetString(20),
            ParseDate(reader.GetString(21)),
            ParseDate(reader.GetString(22)),
            NullableString(reader, 23) is string accepted ? ParseDate(accepted) : null);

    private async Task<ChapterArtworkItem?> QuerySingleAsync(
        string sql,
        Action<DbCommand> bind,
        CancellationToken cancellationToken) =>
        (await QueryAsync(sql, bind, cancellationToken)).FirstOrDefault();

    private Task<IReadOnlyList<ChapterArtworkItem>> QueryAsync(
        string sql,
        Action<DbCommand> bind,
        CancellationToken cancellationToken) =>
        WithConnectionAsync<IReadOnlyList<ChapterArtworkItem>>(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            bind(command);
            var items = new List<ChapterArtworkItem>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(Read(reader));
            }

            return items;
        }, cancellationToken);

    private Task ExecuteAsync(
        string sql,
        Action<DbCommand> bind,
        CancellationToken cancellationToken) =>
        WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            bind(command);
            return await command.ExecuteNonQueryAsync(cancellationToken);
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

    private static string Key(Guid value) => value.ToString("D");

    private static string? Clean(string? value, int maxLength)
    {
        var clean = value?.Trim();
        return string.IsNullOrWhiteSpace(clean)
            ? null
            : clean.Length <= maxLength ? clean : clean[..maxLength];
    }

    private static string? NullableString(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static DateTime ParseDate(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value switch
        {
            null => DBNull.Value,
            DateTime timestamp => timestamp.ToString("O", CultureInfo.InvariantCulture),
            _ => value
        };
        command.Parameters.Add(parameter);
    }
}
