using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Data;

public static class DatabaseMigrationBridge
{
    public const string Epoch2BaselineMigration = "20260922153000_InitialEpoch2Schema";
    private const string ProductVersion = "10.0.12";
    private const string MigrationLockTable = "__EFMigrationsLock";
    private static readonly TimeSpan StaleMigrationLockAge = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MigrationTimeout = TimeSpan.FromMinutes(2);

    private static readonly string[] Epoch2Tables =
    [
        "Anime",
        "Episodes",
        "EpisodeTerms",
        "LibraryRoots",
        "MediaFiles",
        "Reviews",
        "SubtitleCues",
        "SubtitleTracks",
        "Terms",
        "UserTerms"
    ];

    public static async Task UpgradeAsync(
        AppDbContext db,
        CancellationToken cancellationToken = default,
        Action<string>? log = null)
    {
        log?.Invoke("Inspecting SQLite migration state.");

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var existingTables = await ReadTablesAsync(connection, cancellationToken);

            if (existingTables.Contains(MigrationLockTable, StringComparer.Ordinal))
            {
                await RecoverAbandonedMigrationLockAsync(
                    db,
                    connection,
                    log,
                    cancellationToken);
            }

            var hasHistory = existingTables.Contains("__EFMigrationsHistory", StringComparer.Ordinal);

            if (!hasHistory && Epoch2Tables.All(
                    table => existingTables.Contains(table, StringComparer.Ordinal)))
            {
                log?.Invoke("Bridging the pre-migration AniLingo database into the EF migration baseline.");

                await db.Database.ExecuteSqlRawAsync(
                    """
                    CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                        "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                        "ProductVersion" TEXT NOT NULL
                    );
                    """,
                    cancellationToken);

                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT OR IGNORE INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                    VALUES ({Epoch2BaselineMigration}, {ProductVersion});
                    """,
                    cancellationToken);
            }
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }

        log?.Invoke("Applying pending EF Core migrations.");

        using var migrationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        migrationCts.CancelAfter(MigrationTimeout);

        try
        {
            await db.Database.MigrateAsync(migrationCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"SQLite migration did not finish within {MigrationTimeout.TotalSeconds:0} seconds. " +
                "Check Docker logs for another AniLingo instance or a database lock.");
        }

        await MigrateLegacyLearningProfileAsync(db, cancellationToken, log);
        await MigrateLegacyLearningCoursesAsync(db, cancellationToken, log);

        log?.Invoke("Database migrations are complete.");
    }

    private static async Task MigrateLegacyLearningProfileAsync(
        AppDbContext db,
        CancellationToken cancellationToken,
        Action<string>? log)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var userTermColumns = await ReadColumnsAsync(connection, "UserTerms", cancellationToken);
            var reviewColumns = await ReadColumnsAsync(connection, "Reviews", cancellationToken);
            var preferenceColumns = await ReadColumnsAsync(connection, "LearningPreferences", cancellationToken);
            var ownerColumns = await ReadColumnsAsync(connection, "OwnerAccounts", cancellationToken);

            if (!ownerColumns.Contains("Id"))
            {
                return;
            }

            var ownerExists = await ScalarLongAsync(
                connection,
                "SELECT COUNT(*) FROM OwnerAccounts WHERE Id = 'owner';",
                cancellationToken) > 0;

            if (!ownerExists)
            {
                return;
            }

            var migrated = 0;

            if (userTermColumns.Contains("ProfileId") && userTermColumns.Contains("TermId"))
            {
                migrated += await ExecuteAsync(
                    connection,
                    """
                    DELETE FROM UserTerms
                    WHERE ProfileId = 'default'
                      AND EXISTS (
                        SELECT 1
                        FROM UserTerms AS existing
                        WHERE existing.ProfileId = 'owner'
                          AND existing.TermId = UserTerms.TermId
                      );

                    UPDATE UserTerms
                    SET ProfileId = 'owner'
                    WHERE ProfileId = 'default';
                    """,
                    cancellationToken);
            }

            if (reviewColumns.Contains("ProfileId"))
            {
                migrated += await ExecuteAsync(
                    connection,
                    """
                    UPDATE Reviews
                    SET ProfileId = 'owner'
                    WHERE ProfileId = 'default';
                    """,
                    cancellationToken);
            }

            if (preferenceColumns.Contains("ProfileId"))
            {
                migrated += await ExecuteAsync(
                    connection,
                    """
                    DELETE FROM LearningPreferences
                    WHERE ProfileId = 'default'
                      AND EXISTS (
                        SELECT 1
                        FROM LearningPreferences AS existing
                        WHERE existing.ProfileId = 'owner'
                      );

                    UPDATE LearningPreferences
                    SET ProfileId = 'owner'
                    WHERE ProfileId = 'default';
                    """,
                    cancellationToken);
            }

            if (migrated > 0)
            {
                log?.Invoke("Migrated legacy learning profile data to the owner account.");
            }
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task MigrateLegacyLearningCoursesAsync(
        AppDbContext db,
        CancellationToken cancellationToken,
        Action<string>? log)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var tables = await ReadTablesAsync(connection, cancellationToken);
            if (!tables.Contains("LearningUnits")
                || !tables.Contains("LearningVariants")
                || !tables.Contains("LearningCourses")
                || !tables.Contains("LearningCards")
                || !tables.Contains("LearningCardReviews"))
            {
                return;
            }

            var termColumns = await ReadColumnsAsync(
                connection,
                "Terms",
                cancellationToken);

            if (!HasColumns(
                    termColumns,
                    "Id",
                    "Language",
                    "Canonical"))
            {
                log?.Invoke(
                    "Skipped legacy Learning-course copy because this Epoch2 Terms table predates language/text columns.");
                return;
            }

            var readingExpression = termColumns.Contains("Reading")
                ? "\"Reading\""
                : "NULL";
            var meaningAvailable = termColumns.Contains("Meaning");

            var migrated = 0;

            migrated += await ExecuteAsync(
                connection,
                """
                INSERT OR IGNORE INTO "LearningUnits" (
                    "Id", "Kind", "LegacyTermId", "CreatedAt")
                SELECT
                    'legacy-term:' || "Id",
                    'Word',
                    "Id",
                    CURRENT_TIMESTAMP
                FROM "Terms";
                """,
                cancellationToken);

            migrated += await ExecuteAsync(
                connection,
                $"""
                INSERT OR IGNORE INTO "LearningVariants" (
                    "Id", "UnitId", "LanguageTag", "Text", "Reading",
                    "Role", "SourceKind", "CreatedAt")
                SELECT
                    'legacy-source:' || "Id",
                    'legacy-term:' || "Id",
                    "Language",
                    "Canonical",
                    {readingExpression},
                    'Primary',
                    'LegacyTerm',
                    CURRENT_TIMESTAMP
                FROM "Terms"
                WHERE "Language" IS NOT NULL
                  AND trim("Language") <> ''
                  AND "Canonical" IS NOT NULL
                  AND trim("Canonical") <> '';
                """,
                cancellationToken);

            if (meaningAvailable)
            {
                migrated += await ExecuteAsync(
                    connection,
                    """
                    INSERT OR IGNORE INTO "LearningVariants" (
                        "Id", "UnitId", "LanguageTag", "Text", "Reading",
                        "Role", "SourceKind", "CreatedAt")
                    SELECT
                        'legacy-meaning:' || "Id",
                        'legacy-term:' || "Id",
                        'de',
                        "Meaning",
                        NULL,
                        'Meaning',
                        'LegacyMeaning',
                        CURRENT_TIMESTAMP
                    FROM "Terms"
                    WHERE "Meaning" IS NOT NULL
                      AND trim("Meaning") <> '';
                    """,
                    cancellationToken);
            }

            var userTermColumns = await ReadColumnsAsync(
                connection,
                "UserTerms",
                cancellationToken);

            var canCopyCards =
                meaningAvailable
                && HasColumns(
                    userTermColumns,
                    "Id",
                    "ProfileId",
                    "TermId",
                    "State",
                    "IntervalDays",
                    "NextReviewAt",
                    "UpdatedAt");

            if (canCopyCards)
            {
                migrated += await ExecuteAsync(
                    connection,
                    """
                    INSERT OR IGNORE INTO "LearningCourses" (
                        "Id", "ProfileId", "Name", "SourceLanguage", "TargetLanguage",
                        "IsEnabled", "RecognitionEnabled", "ProductionEnabled",
                        "ListeningEnabled", "WritingEnabled", "SentencePracticeEnabled",
                        "CreatedAt", "UpdatedAt")
                    SELECT DISTINCT
                        'legacy-course:' || u."ProfileId" || ':' || t."Language" || ':de',
                        u."ProfileId",
                        'Imported ' || t."Language" || ' → de',
                        t."Language",
                        'de',
                        1,
                        1,
                        0,
                        0,
                        0,
                        1,
                        CURRENT_TIMESTAMP,
                        CURRENT_TIMESTAMP
                    FROM "UserTerms" u
                    INNER JOIN "Terms" t ON t."Id" = u."TermId"
                    WHERE t."Language" IS NOT NULL
                      AND trim(t."Language") <> ''
                      AND t."Language" <> 'de'
                      AND t."Meaning" IS NOT NULL
                      AND trim(t."Meaning") <> '';
                    """,
                    cancellationToken);

                var startedExpression = userTermColumns.Contains("LearningStartedAt")
                    ? "u.\"LearningStartedAt\""
                    : "NULL";
                var queueExpression = userTermColumns.Contains("QueuePosition")
                    ? "u.\"QueuePosition\""
                    : "NULL";

                migrated += await ExecuteAsync(
                    connection,
                    $"""
                    INSERT OR IGNORE INTO "LearningCards" (
                        "Id", "ProfileId", "CourseId", "UnitId",
                        "PromptLanguage", "AnswerLanguage", "Mode", "State",
                        "IntervalDays", "NextReviewAt", "LearningStartedAt",
                        "QueuePosition", "LegacyUserTermId", "CreatedAt", "UpdatedAt")
                    SELECT
                        'legacy-card:' || u."Id",
                        u."ProfileId",
                        'legacy-course:' || u."ProfileId" || ':' || t."Language" || ':de',
                        'legacy-term:' || t."Id",
                        t."Language",
                        'de',
                        'Recognition',
                        CASE u."State"
                            WHEN 1 THEN 'Known'
                            WHEN 2 THEN 'Learning'
                            WHEN 3 THEN 'Saved'
                            WHEN 4 THEN 'Ignored'
                            WHEN 5 THEN 'Suspended'
                            ELSE 'Saved'
                        END,
                        u."IntervalDays",
                        u."NextReviewAt",
                        {startedExpression},
                        {queueExpression},
                        u."Id",
                        u."UpdatedAt",
                        u."UpdatedAt"
                    FROM "UserTerms" u
                    INNER JOIN "Terms" t ON t."Id" = u."TermId"
                    WHERE t."Language" IS NOT NULL
                      AND trim(t."Language") <> ''
                      AND t."Language" <> 'de'
                      AND t."Meaning" IS NOT NULL
                      AND trim(t."Meaning") <> '';
                    """,
                    cancellationToken);

                var reviewColumns = await ReadColumnsAsync(
                    connection,
                    "Reviews",
                    cancellationToken);

                if (HasColumns(
                        reviewColumns,
                        "Id",
                        "ProfileId",
                        "TermId",
                        "Rating",
                        "ReviewedAt",
                        "NextReviewAt"))
                {
                    var eventExpression = reviewColumns.Contains("ClientEventId")
                        ? "r.\"ClientEventId\""
                        : "NULL";

                    migrated += await ExecuteAsync(
                        connection,
                        $"""
                        INSERT OR IGNORE INTO "LearningCardReviews" (
                            "CardId", "Rating", "ClientEventId",
                            "ReviewedAt", "NextReviewAt", "LegacyReviewId")
                        SELECT
                            'legacy-card:' || u."Id",
                            r."Rating",
                            {eventExpression},
                            r."ReviewedAt",
                            r."NextReviewAt",
                            r."Id"
                        FROM "Reviews" r
                        INNER JOIN "UserTerms" u
                            ON u."ProfileId" = r."ProfileId"
                           AND u."TermId" = r."TermId"
                        INNER JOIN "Terms" t ON t."Id" = r."TermId"
                        WHERE t."Language" IS NOT NULL
                          AND trim(t."Language") <> ''
                          AND t."Language" <> 'de'
                          AND t."Meaning" IS NOT NULL
                          AND trim(t."Meaning") <> '';
                        """,
                        cancellationToken);
                }
            }

            if (migrated > 0)
            {
                log?.Invoke(
                    $"Bridged {migrated} legacy Learning-course row(s) into the multilingual model.");
            }
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static bool HasColumns(
        HashSet<string> actual,
        params string[] required) =>
        required.All(actual.Contains);

    private static async Task<HashSet<string>> ReadColumnsAsync(
        System.Data.Common.DbConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table.Replace("\"", "\"\"")}\");";

        var names = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(1))
            {
                names.Add(reader.GetString(1));
            }
        }

        return names;
    }

    private static async Task<long> ScalarLongAsync(
        System.Data.Common.DbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static async Task<int> ExecuteAsync(
        System.Data.Common.DbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RecoverAbandonedMigrationLockAsync(
        AppDbContext db,
        System.Data.Common.DbConnection connection,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT "Timestamp"
            FROM "__EFMigrationsLock"
            WHERE "Id" = 1
            LIMIT 1;
            """;

        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null or DBNull)
        {
            return;
        }

        var rawTimestamp = Convert.ToString(value, CultureInfo.InvariantCulture);
        var timestampValid = DateTimeOffset.TryParse(
            rawTimestamp,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
            out var lockTimestamp);

        if (timestampValid)
        {
            var age = DateTimeOffset.UtcNow - lockTimestamp.ToUniversalTime();
            if (age < StaleMigrationLockAge)
            {
                var remaining = StaleMigrationLockAge - age;
                throw new InvalidOperationException(
                    "A recent SQLite migration lock exists. AniLingo supports one application container per /data volume. " +
                    $"Another instance may still be migrating; retry in about {Math.Ceiling(remaining.TotalSeconds)} seconds.");
            }
        }

        var removed = await db.Database.ExecuteSqlRawAsync(
            """DELETE FROM "__EFMigrationsLock";""",
            cancellationToken);

        if (removed > 0)
        {
            var ageText = timestampValid
                ? $"{Math.Max(0, (DateTimeOffset.UtcNow - lockTimestamp.ToUniversalTime()).TotalSeconds):0}s old"
                : "with an unreadable timestamp";

            log?.Invoke($"Recovered an abandoned EF Core SQLite migration lock ({ageText}).");
        }
    }

    private static async Task<HashSet<string>> ReadTablesAsync(
        System.Data.Common.DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master WHERE type = 'table';";

        var names = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(0))
            {
                names.Add(reader.GetString(0));
            }
        }

        return names;
    }
}
