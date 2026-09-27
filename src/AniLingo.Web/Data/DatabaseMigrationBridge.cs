using System.Data;
using System.Data.Common;
using System.Globalization;
using AniLingo.Web.Features.Kana;
using AniLingo.Web.Features.Vocabulary;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AniLingo.Web.Data;

public static class DatabaseMigrationBridge
{
    public const string Epoch2BaselineMigration = "20260922153000_InitialEpoch2Schema";
    public const string UniversalLearningCoursesMigration = "20260926080000_AddUniversalLearningCourses";
    public const string RetireLegacyLearningStateMigration = "20260926080500_RetireLegacyLearningState";
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
                log?.Invoke("Bridging the pre-migration Jularr database into the EF migration baseline.");

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
            var pending = await db.Database.GetPendingMigrationsAsync(migrationCts.Token);

            if (pending.Contains(RetireLegacyLearningStateMigration, StringComparer.Ordinal))
            {
                // One-time conversion at the migration boundary: create the
                // Learning tables, copy the legacy learning state into them, then
                // let the retirement migration drop the legacy tables.
                await db.GetService<IMigrator>().MigrateAsync(
                    UniversalLearningCoursesMigration,
                    migrationCts.Token);
                await ConvertLegacyLearningStateAsync(db, migrationCts.Token, log);
            }

            await db.Database.MigrateAsync(migrationCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"SQLite migration did not finish within {MigrationTimeout.TotalSeconds:0} seconds. " +
                "Check Docker logs for another Jularr instance or a database lock.");
        }

        await MigrateLegacyLearningProfileAsync(db, cancellationToken, log);

        log?.Invoke("Database migrations are complete.");
    }

    /// <summary>
    /// Moves learning data of the pre-account 'default' profile to the owner
    /// once the owner account exists. Owner data wins where both profiles hold
    /// the same course pair and card.
    /// </summary>
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

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var migrated = await ExecuteAsync(
                connection,
                transaction,
                """
                UPDATE "LearningCards"
                SET "CourseId" = (
                        SELECT o."Id"
                        FROM "LearningCourses" o
                        INNER JOIN "LearningCourses" d ON d."Id" = "LearningCards"."CourseId"
                        WHERE o."ProfileId" = 'owner'
                          AND o."SourceLanguage" = d."SourceLanguage"
                          AND o."TargetLanguage" = d."TargetLanguage"),
                    "ProfileId" = 'owner'
                WHERE "ProfileId" = 'default'
                  AND EXISTS (
                        SELECT 1
                        FROM "LearningCourses" o
                        INNER JOIN "LearningCourses" d ON d."Id" = "LearningCards"."CourseId"
                        WHERE o."ProfileId" = 'owner'
                          AND o."SourceLanguage" = d."SourceLanguage"
                          AND o."TargetLanguage" = d."TargetLanguage")
                  AND NOT EXISTS (
                        SELECT 1
                        FROM "LearningCards" existing
                        INNER JOIN "LearningCourses" o ON o."Id" = existing."CourseId"
                        INNER JOIN "LearningCourses" d ON d."Id" = "LearningCards"."CourseId"
                        WHERE o."ProfileId" = 'owner'
                          AND o."SourceLanguage" = d."SourceLanguage"
                          AND o."TargetLanguage" = d."TargetLanguage"
                          AND existing."UnitId" = "LearningCards"."UnitId"
                          AND existing."Mode" = "LearningCards"."Mode");

                DELETE FROM "LearningCards"
                WHERE "ProfileId" = 'default'
                  AND EXISTS (
                        SELECT 1
                        FROM "LearningCourses" o
                        INNER JOIN "LearningCourses" d ON d."Id" = "LearningCards"."CourseId"
                        WHERE o."ProfileId" = 'owner'
                          AND o."SourceLanguage" = d."SourceLanguage"
                          AND o."TargetLanguage" = d."TargetLanguage");

                DELETE FROM "LearningCourses"
                WHERE "ProfileId" = 'default'
                  AND EXISTS (
                        SELECT 1
                        FROM "LearningCourses" o
                        WHERE o."ProfileId" = 'owner'
                          AND o."SourceLanguage" = "LearningCourses"."SourceLanguage"
                          AND o."TargetLanguage" = "LearningCourses"."TargetLanguage");

                UPDATE "LearningCourses"
                SET "IsPrimary" = 0
                WHERE "ProfileId" = 'default'
                  AND "IsPrimary" = 1
                  AND EXISTS (
                        SELECT 1
                        FROM "LearningCourses" o
                        WHERE o."ProfileId" = 'owner'
                          AND o."SourceLanguage" = "LearningCourses"."SourceLanguage"
                          AND o."IsPrimary" = 1);

                UPDATE "LearningCourses"
                SET "ProfileId" = 'owner'
                WHERE "ProfileId" = 'default';

                UPDATE "LearningCards"
                SET "ProfileId" = 'owner'
                WHERE "ProfileId" = 'default';

                DELETE FROM "LearningCardReviews"
                WHERE "CardId" NOT IN (SELECT "Id" FROM "LearningCards");

                UPDATE OR IGNORE "LearningCardReviews"
                SET "ProfileId" = 'owner'
                WHERE "ProfileId" = 'default';

                DELETE FROM "LearningCardReviews"
                WHERE "ProfileId" = 'default';

                DELETE FROM "LearningPreferences"
                WHERE "ProfileId" = 'default'
                  AND EXISTS (
                    SELECT 1
                    FROM "LearningPreferences" AS existing
                    WHERE existing."ProfileId" = 'owner'
                  );

                UPDATE "LearningPreferences"
                SET "ProfileId" = 'owner'
                WHERE "ProfileId" = 'default';
                """,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);

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

    /// <summary>
    /// Copies the legacy Term/UserTerm/Review learning state into directional
    /// Learning cards. Runs only while <see cref="RetireLegacyLearningStateMigration"/>
    /// is pending, in one transaction and with deterministic IDs (card = UserTerm
    /// ID, unit = Term ID, review = Review ID), so an interrupted upgrade can be
    /// retried safely before the legacy tables are dropped.
    /// </summary>
    private static async Task ConvertLegacyLearningStateAsync(
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
            if (!tables.Contains("UserTerms"))
            {
                return;
            }

            var userTermCount = await ScalarLongAsync(
                connection,
                """SELECT COUNT(*) FROM "UserTerms";""",
                cancellationToken);

            if (userTermCount == 0)
            {
                return;
            }

            var termColumns = await ReadColumnsAsync(connection, "Terms", cancellationToken);
            var userTermColumns = await ReadColumnsAsync(connection, "UserTerms", cancellationToken);
            var reviewColumns = await ReadColumnsAsync(connection, "Reviews", cancellationToken);

            if (!HasColumns(termColumns, "Id", "Language", "Canonical")
                || !HasColumns(
                    userTermColumns,
                    "Id",
                    "ProfileId",
                    "TermId",
                    "State",
                    "IntervalDays",
                    "NextReviewAt",
                    "UpdatedAt"))
            {
                throw new InvalidOperationException(
                    "Legacy learning progress cannot be converted: the Terms/UserTerms tables do not have the expected Epoch 2 columns.");
            }

            var reviewsConvertible = HasColumns(
                reviewColumns,
                "Id",
                "ProfileId",
                "TermId",
                "Rating",
                "ReviewedAt",
                "NextReviewAt");

            if (!reviewsConvertible
                && await ScalarLongAsync(connection, """SELECT COUNT(*) FROM "Reviews";""", cancellationToken) > 0)
            {
                throw new InvalidOperationException(
                    "Legacy review history cannot be converted: the Reviews table does not have the expected Epoch 2 columns.");
            }

            var reading = termColumns.Contains("Reading") ? "t.\"Reading\"" : "NULL";
            var meaning = termColumns.Contains("Meaning") ? "t.\"Meaning\"" : "NULL";
            var started = userTermColumns.Contains("LearningStartedAt") ? "u.\"LearningStartedAt\"" : "NULL";
            var queue = userTermColumns.Contains("QueuePosition") ? "u.\"QueuePosition\"" : "NULL";
            var clientEvent = reviewColumns.Contains("ClientEventId") ? "r.\"ClientEventId\"" : "NULL";

            // Kana used to be catalog Terms in the pseudo-language IdNamespace. They
            // become Script units in a non-primary ja → ja-Latn course.
            const string isKana = "t.\"Language\" = $kana";
            var sourceLanguage = $"CASE WHEN {isKana} THEN $kanaPrompt ELSE t.\"Language\" END";
            var targetLanguage = $"CASE WHEN {isKana} THEN $kanaAnswer ELSE $meaningLanguage END";
            const string newGuid =
                "hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(6))";

            (string, object?)[] parameters =
            [
                ("$kana", KanaCatalog.IdNamespace),
                ("$kanaPrompt", KanaCatalog.PromptLanguage),
                ("$kanaAnswer", KanaCatalog.AnswerLanguage),
                ("$kanaCourse", KanaCatalog.CourseName),
                ("$meaningLanguage", Term.MeaningLanguage),
                ("$now", DateTime.UtcNow)
            ];

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var orphans = await ExecuteAsync(
                connection,
                transaction,
                """
                DELETE FROM "UserTerms"
                WHERE "TermId" NOT IN (SELECT "Id" FROM "Terms");
                """,
                cancellationToken);

            await ExecuteAsync(
                connection,
                transaction,
                $"""
                INSERT OR IGNORE INTO "LearningUnits" ("Id", "Kind", "TermId", "CreatedAt")
                SELECT
                    t."Id",
                    CASE WHEN {isKana} THEN 'Script' ELSE 'Word' END,
                    CASE WHEN {isKana} THEN NULL ELSE t."Id" END,
                    $now
                FROM "Terms" t
                WHERE t."Id" IN (SELECT "TermId" FROM "UserTerms");

                INSERT OR IGNORE INTO "LearningVariants" (
                    "Id", "UnitId", "LanguageTag", "Text", "Reading", "Role", "SourceKind", "CreatedAt")
                SELECT
                    {newGuid},
                    t."Id",
                    {sourceLanguage},
                    trim(t."Canonical"),
                    CASE WHEN {isKana} THEN NULL ELSE {reading} END,
                    'Primary',
                    CASE WHEN {isKana} THEN 'ScriptCatalog' ELSE 'Term' END,
                    $now
                FROM "Terms" t
                WHERE t."Id" IN (SELECT "Id" FROM "LearningUnits");

                INSERT OR IGNORE INTO "LearningVariants" (
                    "Id", "UnitId", "LanguageTag", "Text", "Reading", "Role", "SourceKind", "CreatedAt")
                SELECT
                    {newGuid},
                    t."Id",
                    $kanaAnswer,
                    trim({reading}),
                    NULL,
                    'Primary',
                    'ScriptCatalog',
                    $now
                FROM "Terms" t
                WHERE {isKana}
                  AND t."Id" IN (SELECT "Id" FROM "LearningUnits")
                  AND trim(coalesce({reading}, '')) <> '';

                INSERT OR IGNORE INTO "LearningVariants" (
                    "Id", "UnitId", "LanguageTag", "Text", "Reading", "Role", "SourceKind", "CreatedAt")
                SELECT
                    {newGuid},
                    t."Id",
                    $meaningLanguage,
                    trim({meaning}),
                    NULL,
                    'Meaning',
                    'Dictionary',
                    $now
                FROM "Terms" t
                WHERE NOT ({isKana})
                  AND t."Language" <> $meaningLanguage
                  AND t."Id" IN (SELECT "Id" FROM "LearningUnits")
                  AND trim(coalesce({meaning}, '')) <> '';

                INSERT OR IGNORE INTO "LearningCourses" (
                    "Id", "ProfileId", "Name", "SourceLanguage", "TargetLanguage",
                    "IsEnabled", "IsPrimary", "RecognitionEnabled", "ProductionEnabled",
                    "ListeningEnabled", "WritingEnabled", "SentencePracticeEnabled",
                    "CreatedAt", "UpdatedAt")
                SELECT
                    {newGuid},
                    pairs."ProfileId",
                    CASE WHEN pairs."IsKana" = 1 THEN $kanaCourse
                         ELSE pairs."SourceLanguage" || ' → ' || pairs."TargetLanguage" END,
                    pairs."SourceLanguage",
                    pairs."TargetLanguage",
                    1,
                    CASE WHEN pairs."IsKana" = 1 THEN 0 ELSE 1 END,
                    1,
                    0,
                    0,
                    0,
                    CASE WHEN pairs."IsKana" = 1 THEN 0 ELSE 1 END,
                    $now,
                    $now
                FROM (
                    SELECT DISTINCT
                        u."ProfileId" AS "ProfileId",
                        {sourceLanguage} AS "SourceLanguage",
                        {targetLanguage} AS "TargetLanguage",
                        CASE WHEN {isKana} THEN 1 ELSE 0 END AS "IsKana"
                    FROM "UserTerms" u
                    INNER JOIN "Terms" t ON t."Id" = u."TermId"
                ) pairs;

                INSERT OR IGNORE INTO "LearningCards" (
                    "Id", "ProfileId", "CourseId", "UnitId",
                    "PromptLanguage", "AnswerLanguage", "Mode", "State",
                    "IntervalDays", "NextReviewAt", "LearningStartedAt",
                    "QueuePosition", "CreatedAt", "UpdatedAt")
                SELECT
                    u."Id",
                    u."ProfileId",
                    c."Id",
                    t."Id",
                    c."SourceLanguage",
                    c."TargetLanguage",
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
                    {started},
                    {queue},
                    u."UpdatedAt",
                    u."UpdatedAt"
                FROM "UserTerms" u
                INNER JOIN "Terms" t ON t."Id" = u."TermId"
                INNER JOIN "LearningCourses" c
                    ON c."ProfileId" = u."ProfileId"
                   AND c."SourceLanguage" = {sourceLanguage}
                   AND c."TargetLanguage" = {targetLanguage};
                """,
                cancellationToken,
                parameters);

            var reviews = 0;
            if (reviewsConvertible)
            {
                reviews = await ExecuteAsync(
                    connection,
                    transaction,
                    $"""
                    INSERT OR IGNORE INTO "LearningCardReviews" (
                        "Id", "ProfileId", "CardId", "Rating", "ClientEventId",
                        "ReviewedAt", "NextReviewAt")
                    SELECT
                        r."Id",
                        r."ProfileId",
                        u."Id",
                        r."Rating",
                        {clientEvent},
                        r."ReviewedAt",
                        r."NextReviewAt"
                    FROM "Reviews" r
                    INNER JOIN "UserTerms" u
                        ON u."ProfileId" = r."ProfileId"
                       AND u."TermId" = r."TermId";
                    """,
                    cancellationToken);
            }

            await ExecuteAsync(
                connection,
                transaction,
                """
                DELETE FROM "Terms" WHERE "Language" = $kana;
                """,
                cancellationToken,
                parameters);

            await transaction.CommitAsync(cancellationToken);

            log?.Invoke(
                $"Converted {userTermCount - orphans} legacy learning item(s) and {reviews} review(s) into Learning cards." +
                (orphans > 0 ? $" Removed {orphans} item(s) whose catalog term no longer existed." : ""));
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
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

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
                    "A recent SQLite migration lock exists. Jularr supports one application container per /data volume. " +
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
