using System.Data;
using System.Data.Common;
using System.Globalization;
using AniLingo.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Learning.Courses;

public sealed class LearningCourseStore(AppDbContext db)
{
    public async Task<LearningCourseSnapshot> CreateAsync(
        string profileId,
        string sourceLanguage,
        string targetLanguage,
        string? name,
        LearningCourseOptions? options,
        CancellationToken cancellationToken)
    {
        ValidateProfile(profileId);

        var source = LearningLanguageTag.Normalize(sourceLanguage);
        var target = LearningLanguageTag.Normalize(targetLanguage);

        if (source.Equals(target, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Source and target languages must be different.");
        }

        options ??= new LearningCourseOptions();

        var course = new LearningCourseSnapshot(
            Guid.NewGuid().ToString("N"),
            profileId.Trim(),
            string.IsNullOrWhiteSpace(name)
                ? $"{source} → {target}"
                : name.Trim(),
            source,
            target,
            true,
            options,
            DateTime.UtcNow,
            DateTime.UtcNow);

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO "LearningCourses" (
                    "Id", "ProfileId", "Name", "SourceLanguage", "TargetLanguage",
                    "IsEnabled", "RecognitionEnabled", "ProductionEnabled",
                    "ListeningEnabled", "WritingEnabled", "SentencePracticeEnabled",
                    "CreatedAt", "UpdatedAt")
                VALUES (
                    $id, $profileId, $name, $sourceLanguage, $targetLanguage,
                    1, $recognition, $production,
                    $listening, $writing, $sentences,
                    $createdAt, $updatedAt);
                """;

            Add(command, "$id", course.Id);
            Add(command, "$profileId", course.ProfileId);
            Add(command, "$name", course.Name);
            Add(command, "$sourceLanguage", course.SourceLanguage);
            Add(command, "$targetLanguage", course.TargetLanguage);
            Add(command, "$recognition", options.RecognitionEnabled ? 1 : 0);
            Add(command, "$production", options.ProductionEnabled ? 1 : 0);
            Add(command, "$listening", options.ListeningEnabled ? 1 : 0);
            Add(command, "$writing", options.WritingEnabled ? 1 : 0);
            Add(command, "$sentences", options.SentencePracticeEnabled ? 1 : 0);
            Add(command, "$createdAt", course.CreatedAt);
            Add(command, "$updatedAt", course.UpdatedAt);

            try
            {
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (Exception exception) when (
                exception.Message.Contains(
                    "UNIQUE constraint failed",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"A {source} → {target} learning course already exists for this profile.",
                    exception);
            }

            return course;
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task<IReadOnlyList<LearningCourseSnapshot>> ListAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        ValidateProfile(profileId);

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    "Id", "ProfileId", "Name", "SourceLanguage", "TargetLanguage",
                    "IsEnabled", "RecognitionEnabled", "ProductionEnabled",
                    "ListeningEnabled", "WritingEnabled", "SentencePracticeEnabled",
                    "CreatedAt", "UpdatedAt"
                FROM "LearningCourses"
                WHERE "ProfileId" = $profileId
                ORDER BY "IsEnabled" DESC, "CreatedAt", "Name";
                """;
            Add(command, "$profileId", profileId.Trim());

            var result = new List<LearningCourseSnapshot>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(ReadCourse(reader));
            }

            return result;
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task<LearningCourseSnapshot?> GetAsync(
        string profileId,
        string courseId,
        CancellationToken cancellationToken)
    {
        ValidateProfile(profileId);

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    "Id", "ProfileId", "Name", "SourceLanguage", "TargetLanguage",
                    "IsEnabled", "RecognitionEnabled", "ProductionEnabled",
                    "ListeningEnabled", "WritingEnabled", "SentencePracticeEnabled",
                    "CreatedAt", "UpdatedAt"
                FROM "LearningCourses"
                WHERE "ProfileId" = $profileId
                  AND "Id" = $courseId
                LIMIT 1;
                """;
            Add(command, "$profileId", profileId.Trim());
            Add(command, "$courseId", courseId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken)
                ? ReadCourse(reader)
                : null;
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task UpdateAsync(
        string profileId,
        string courseId,
        string? name,
        bool isEnabled,
        LearningCourseOptions options,
        CancellationToken cancellationToken)
    {
        ValidateProfile(profileId);

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE "LearningCourses"
                SET "Name" = CASE
                        WHEN $name IS NULL OR trim($name) = '' THEN "Name"
                        ELSE trim($name)
                    END,
                    "IsEnabled" = $isEnabled,
                    "RecognitionEnabled" = $recognition,
                    "ProductionEnabled" = $production,
                    "ListeningEnabled" = $listening,
                    "WritingEnabled" = $writing,
                    "SentencePracticeEnabled" = $sentences,
                    "UpdatedAt" = $updatedAt
                WHERE "ProfileId" = $profileId
                  AND "Id" = $courseId;
                """;

            Add(command, "$name", name);
            Add(command, "$isEnabled", isEnabled ? 1 : 0);
            Add(command, "$recognition", options.RecognitionEnabled ? 1 : 0);
            Add(command, "$production", options.ProductionEnabled ? 1 : 0);
            Add(command, "$listening", options.ListeningEnabled ? 1 : 0);
            Add(command, "$writing", options.WritingEnabled ? 1 : 0);
            Add(command, "$sentences", options.SentencePracticeEnabled ? 1 : 0);
            Add(command, "$updatedAt", DateTime.UtcNow);
            Add(command, "$profileId", profileId.Trim());
            Add(command, "$courseId", courseId);

            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                throw new KeyNotFoundException(
                    "Learning course was not found for this profile.");
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

    public async Task<LearningUnitCreateResult> CreateUnitAsync(
        LearningUnitKind kind,
        IReadOnlyList<LearningVariantInput> variants,
        CancellationToken cancellationToken)
    {
        if (variants.Count == 0)
        {
            throw new ArgumentException(
                "At least one language variant is required.",
                nameof(variants));
        }

        var normalized = variants
            .Select(variant =>
            {
                var language = LearningLanguageTag.Normalize(variant.LanguageTag);
                var text = variant.Text?.Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    throw new ArgumentException(
                        "Learning variant text is required.",
                        nameof(variants));
                }

                return variant with
                {
                    LanguageTag = language,
                    Text = text,
                    Reading = string.IsNullOrWhiteSpace(variant.Reading)
                        ? null
                        : variant.Reading.Trim(),
                    Role = string.IsNullOrWhiteSpace(variant.Role)
                        ? "Primary"
                        : variant.Role.Trim(),
                    SourceKind = string.IsNullOrWhiteSpace(variant.SourceKind)
                        ? "Manual"
                        : variant.SourceKind.Trim()
                };
            })
            .ToArray();

        var duplicate = normalized
            .GroupBy(
                x => (x.LanguageTag, x.Text),
                EqualityComparer<(string, string)>.Default)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                "Duplicate language/text variants are not allowed.",
                nameof(variants));
        }

        var unitId = Guid.NewGuid().ToString("N");
        var createdAt = DateTime.UtcNow;

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var transaction =
                await connection.BeginTransactionAsync(cancellationToken);

            await using (var unitCommand = connection.CreateCommand())
            {
                unitCommand.Transaction = transaction;
                unitCommand.CommandText =
                    """
                    INSERT INTO "LearningUnits" (
                        "Id", "Kind", "LegacyTermId", "CreatedAt")
                    VALUES (
                        $id, $kind, NULL, $createdAt);
                    """;
                Add(unitCommand, "$id", unitId);
                Add(unitCommand, "$kind", kind.ToString());
                Add(unitCommand, "$createdAt", createdAt);
                await unitCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            var snapshots = new List<LearningVariantSnapshot>(normalized.Length);
            foreach (var variant in normalized)
            {
                var variantId = Guid.NewGuid().ToString("N");

                await using var variantCommand = connection.CreateCommand();
                variantCommand.Transaction = transaction;
                variantCommand.CommandText =
                    """
                    INSERT INTO "LearningVariants" (
                        "Id", "UnitId", "LanguageTag", "Text", "Reading",
                        "Role", "SourceKind", "CreatedAt")
                    VALUES (
                        $id, $unitId, $languageTag, $text, $reading,
                        $role, $sourceKind, $createdAt);
                    """;
                Add(variantCommand, "$id", variantId);
                Add(variantCommand, "$unitId", unitId);
                Add(variantCommand, "$languageTag", variant.LanguageTag);
                Add(variantCommand, "$text", variant.Text);
                Add(variantCommand, "$reading", variant.Reading);
                Add(variantCommand, "$role", variant.Role);
                Add(variantCommand, "$sourceKind", variant.SourceKind);
                Add(variantCommand, "$createdAt", createdAt);
                await variantCommand.ExecuteNonQueryAsync(cancellationToken);

                snapshots.Add(new LearningVariantSnapshot(
                    variantId,
                    unitId,
                    variant.LanguageTag,
                    variant.Text,
                    variant.Reading,
                    variant.Role,
                    variant.SourceKind));
            }

            await transaction.CommitAsync(cancellationToken);

            return new LearningUnitCreateResult(
                new LearningUnitSnapshot(
                    unitId,
                    kind,
                    null,
                    createdAt),
                snapshots);
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task<IReadOnlyList<LearningCardSnapshot>> EnsureCourseCardsAsync(
        string profileId,
        string courseId,
        string unitId,
        LearningCardState initialState,
        CancellationToken cancellationToken)
    {
        ValidateProfile(profileId);

        var course = await GetAsync(
            profileId,
            courseId,
            cancellationToken)
            ?? throw new KeyNotFoundException(
                "Learning course was not found for this profile.");

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await using (var variantCommand = connection.CreateCommand())
            {
                variantCommand.CommandText =
                    """
                    SELECT DISTINCT "LanguageTag"
                    FROM "LearningVariants"
                    WHERE "UnitId" = $unitId;
                    """;
                Add(variantCommand, "$unitId", unitId);

                await using var reader =
                    await variantCommand.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    languages.Add(reader.GetString(0));
                }
            }

            if (!languages.Contains(course.SourceLanguage)
                || !languages.Contains(course.TargetLanguage))
            {
                throw new InvalidOperationException(
                    "The learning unit needs variants for both course languages before cards can be created.");
            }

            var requested = new List<(LearningCardMode Mode, string Prompt, string Answer)>();
            if (course.Options.RecognitionEnabled)
            {
                requested.Add((
                    LearningCardMode.Recognition,
                    course.SourceLanguage,
                    course.TargetLanguage));
            }

            if (course.Options.ProductionEnabled)
            {
                requested.Add((
                    LearningCardMode.Production,
                    course.TargetLanguage,
                    course.SourceLanguage));
            }

            if (course.Options.ListeningEnabled)
            {
                requested.Add((
                    LearningCardMode.Listening,
                    course.SourceLanguage,
                    course.TargetLanguage));
            }

            if (course.Options.WritingEnabled)
            {
                requested.Add((
                    LearningCardMode.Writing,
                    course.TargetLanguage,
                    course.SourceLanguage));
            }

            var now = DateTime.UtcNow;
            foreach (var requestedCard in requested)
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    INSERT OR IGNORE INTO "LearningCards" (
                        "Id", "ProfileId", "CourseId", "UnitId",
                        "PromptLanguage", "AnswerLanguage", "Mode", "State",
                        "IntervalDays", "NextReviewAt", "LearningStartedAt",
                        "QueuePosition", "LegacyUserTermId", "CreatedAt", "UpdatedAt")
                    VALUES (
                        $id, $profileId, $courseId, $unitId,
                        $promptLanguage, $answerLanguage, $mode, $state,
                        0, NULL, NULL,
                        NULL, NULL, $createdAt, $updatedAt);
                    """;
                Add(command, "$id", Guid.NewGuid().ToString("N"));
                Add(command, "$profileId", profileId.Trim());
                Add(command, "$courseId", courseId);
                Add(command, "$unitId", unitId);
                Add(command, "$promptLanguage", requestedCard.Prompt);
                Add(command, "$answerLanguage", requestedCard.Answer);
                Add(command, "$mode", requestedCard.Mode.ToString());
                Add(command, "$state", initialState.ToString());
                Add(command, "$createdAt", now);
                Add(command, "$updatedAt", now);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }

        return await ListCardsForUnitAsync(
            profileId,
            courseId,
            unitId,
            cancellationToken);
    }

    public async Task<LearningContextAnchor> AddContextAsync(
        string unitId,
        LearningContextInput input,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(unitId))
        {
            throw new ArgumentException(
                "Learning unit ID is required.",
                nameof(unitId));
        }

        if (string.IsNullOrWhiteSpace(input.SourceType))
        {
            throw new ArgumentException(
                "Context source type is required.",
                nameof(input));
        }

        if (string.IsNullOrWhiteSpace(input.SourceKey))
        {
            throw new ArgumentException(
                "Context source key is required.",
                nameof(input));
        }

        if (string.IsNullOrWhiteSpace(input.Text))
        {
            throw new ArgumentException(
                "Context text is required.",
                nameof(input));
        }

        var language = LearningLanguageTag.Normalize(input.LanguageTag);
        var createdAt = DateTime.UtcNow;
        var anchor = new LearningContextAnchor(
            Guid.NewGuid().ToString("N"),
            unitId,
            input.SourceType.Trim(),
            input.SourceKey.Trim(),
            string.IsNullOrWhiteSpace(input.PositionKey)
                ? null
                : input.PositionKey.Trim(),
            language,
            input.Text.Trim(),
            createdAt);

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO "LearningContexts" (
                    "Id", "UnitId", "SourceType", "SourceKey",
                    "PositionKey", "LanguageTag", "Text", "CreatedAt")
                VALUES (
                    $id, $unitId, $sourceType, $sourceKey,
                    $positionKey, $languageTag, $text, $createdAt);
                """;
            Add(command, "$id", anchor.Id);
            Add(command, "$unitId", anchor.UnitId);
            Add(command, "$sourceType", anchor.SourceType);
            Add(command, "$sourceKey", anchor.SourceKey);
            Add(command, "$positionKey", anchor.PositionKey);
            Add(command, "$languageTag", anchor.LanguageTag);
            Add(command, "$text", anchor.Text);
            Add(command, "$createdAt", anchor.CreatedAt);
            await command.ExecuteNonQueryAsync(cancellationToken);

            return anchor;
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task<IReadOnlyList<LearningContextAnchor>> ListContextsAsync(
        string unitId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(unitId))
        {
            throw new ArgumentException(
                "Learning unit ID is required.",
                nameof(unitId));
        }

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    "Id", "UnitId", "SourceType", "SourceKey",
                    "PositionKey", "LanguageTag", "Text", "CreatedAt"
                FROM "LearningContexts"
                WHERE "UnitId" = $unitId
                ORDER BY "CreatedAt", "Id";
                """;
            Add(command, "$unitId", unitId);

            var result = new List<LearningContextAnchor>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(new LearningContextAnchor(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    ParseDate(reader.GetString(7))));
            }

            return result;
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task<IReadOnlyList<LearningVariantSnapshot>> ListVariantsAsync(
        string unitId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(unitId))
        {
            throw new ArgumentException(
                "Learning unit ID is required.",
                nameof(unitId));
        }

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    "Id", "UnitId", "LanguageTag", "Text", "Reading",
                    "Role", "SourceKind"
                FROM "LearningVariants"
                WHERE "UnitId" = $unitId
                ORDER BY "LanguageTag", "Role", "Text";
                """;
            Add(command, "$unitId", unitId);

            var result = new List<LearningVariantSnapshot>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(new LearningVariantSnapshot(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6)));
            }

            return result;
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<IReadOnlyList<LearningCardSnapshot>> ListCardsForUnitAsync(
        string profileId,
        string courseId,
        string unitId,
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
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    "Id", "ProfileId", "CourseId", "UnitId",
                    "PromptLanguage", "AnswerLanguage", "Mode", "State",
                    "IntervalDays", "NextReviewAt", "LegacyUserTermId"
                FROM "LearningCards"
                WHERE "ProfileId" = $profileId
                  AND "CourseId" = $courseId
                  AND "UnitId" = $unitId
                ORDER BY "Mode", "Id";
                """;
            Add(command, "$profileId", profileId.Trim());
            Add(command, "$courseId", courseId);
            Add(command, "$unitId", unitId);

            var result = new List<LearningCardSnapshot>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(ReadCard(reader));
            }

            return result;
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task<IReadOnlyList<LearningCardSnapshot>> ListCardsAsync(
        string profileId,
        string courseId,
        CancellationToken cancellationToken)
    {
        ValidateProfile(profileId);

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    "Id", "ProfileId", "CourseId", "UnitId",
                    "PromptLanguage", "AnswerLanguage", "Mode", "State",
                    "IntervalDays", "NextReviewAt", "LegacyUserTermId"
                FROM "LearningCards"
                WHERE "ProfileId" = $profileId
                  AND "CourseId" = $courseId
                ORDER BY "UpdatedAt", "Id";
                """;
            Add(command, "$profileId", profileId.Trim());
            Add(command, "$courseId", courseId);

            var result = new List<LearningCardSnapshot>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(ReadCard(reader));
            }

            return result;
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static LearningCardSnapshot ReadCard(DbDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            Enum.Parse<LearningCardMode>(
                reader.GetString(6),
                ignoreCase: true),
            Enum.Parse<LearningCardState>(
                reader.GetString(7),
                ignoreCase: true),
            reader.GetInt32(8),
            reader.IsDBNull(9)
                ? null
                : ParseDate(reader.GetString(9)),
            reader.IsDBNull(10)
                ? null
                : reader.GetString(10));

    private static LearningCourseSnapshot ReadCourse(DbDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt32(5) != 0,
            new LearningCourseOptions(
                reader.GetInt32(6) != 0,
                reader.GetInt32(7) != 0,
                reader.GetInt32(8) != 0,
                reader.GetInt32(9) != 0,
                reader.GetInt32(10) != 0),
            ParseDate(reader.GetString(11)),
            ParseDate(reader.GetString(12)));

    private static DateTime ParseDate(string value) =>
        DateTime.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind | DateTimeStyles.AllowWhiteSpaces);

    private static void ValidateProfile(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException(
                "Profile ID is required.",
                nameof(profileId));
        }
    }

    private static void Add(
        DbCommand command,
        string name,
        object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
