using System.Data;
using System.Data.Common;
using AniLingo.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Learning;

public enum LearningMode
{
    Off = 0,
    LanguageTools = 1,
    Study = 2,
    Custom = 3
}

public enum LearningCapability
{
    LanguageLookup = 1,
    ReadingAids = 2,
    Translation = 3,
    AiExplanations = 4,
    Vocabulary = 5,
    Reviews = 6,
    SentencePractice = 7,
    ScriptTrainer = 8,
    Progress = 9,
    HomeWidget = 10,
    ContentMetrics = 11,
    PreparationSuggestions = 12,
    PlayerTools = 13,
    ReaderTools = 14
}

public enum LearningScopeKind
{
    Profile = 1,
    MediaType = 2,
    Work = 3,
    Content = 4
}

public enum LearningMediaType
{
    Anime = 1,
    Novel = 2,
    Book = 3,
    Manga = 4
}

public sealed record LearningScopeRef(
    LearningScopeKind Kind,
    string Key)
{
    public static LearningScopeRef Profile { get; } =
        new(LearningScopeKind.Profile, "*");

    public static LearningScopeRef ForMedia(LearningMediaType mediaType) =>
        new(LearningScopeKind.MediaType, mediaType.ToString());

    public static LearningScopeRef ForWork(
        LearningMediaType mediaType,
        string workKey) =>
        new(
            LearningScopeKind.Work,
            NormalizeCompositeKey(mediaType, workKey));

    public static LearningScopeRef ForContent(
        LearningMediaType mediaType,
        string contentKey) =>
        new(
            LearningScopeKind.Content,
            NormalizeCompositeKey(mediaType, contentKey));

    private static string NormalizeCompositeKey(
        LearningMediaType mediaType,
        string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Scope key is required.", nameof(key));
        }

        return $"{mediaType}:{key.Trim()}";
    }
}

public sealed record LearningScopeContext(
    LearningMediaType MediaType,
    string? WorkKey = null,
    string? ContentKey = null)
{
    public IReadOnlyList<LearningScopeRef> BuildChain()
    {
        var scopes = new List<LearningScopeRef>
        {
            LearningScopeRef.Profile,
            LearningScopeRef.ForMedia(MediaType)
        };

        if (!string.IsNullOrWhiteSpace(WorkKey))
        {
            scopes.Add(LearningScopeRef.ForWork(MediaType, WorkKey));
        }

        if (!string.IsNullOrWhiteSpace(ContentKey))
        {
            scopes.Add(LearningScopeRef.ForContent(MediaType, ContentKey));
        }

        return scopes;
    }
}

public sealed record LearningScopeSettingsSnapshot(
    LearningScopeRef Scope,
    LearningMode? ModeOverride,
    IReadOnlyDictionary<LearningCapability, bool?> Capabilities)
{
    public bool? GetOverride(LearningCapability capability) =>
        Capabilities.GetValueOrDefault(capability);
}

public sealed class LearningResolvedSettings(
    LearningMode mode,
    IReadOnlyDictionary<LearningCapability, bool> capabilities)
{
    public LearningMode Mode { get; } = mode;

    public bool IsEnabled(LearningCapability capability) =>
        capabilities.GetValueOrDefault(capability);

    public IReadOnlyDictionary<LearningCapability, bool> Capabilities =>
        capabilities;

    public bool HasAnyVisibleLearning =>
        capabilities.Any(x => x.Value);
}

public static class LearningConfigurationDefaults
{
    private static readonly IReadOnlyDictionary<LearningMode, IReadOnlyDictionary<LearningCapability, bool>>
        Defaults = new Dictionary<LearningMode, IReadOnlyDictionary<LearningCapability, bool>>
        {
            [LearningMode.Off] = Build(),
            [LearningMode.LanguageTools] = Build(
                LearningCapability.LanguageLookup,
                LearningCapability.ReadingAids,
                LearningCapability.Translation,
                LearningCapability.AiExplanations,
                LearningCapability.PlayerTools,
                LearningCapability.ReaderTools),
            [LearningMode.Study] = Build(
                LearningCapability.LanguageLookup,
                LearningCapability.ReadingAids,
                LearningCapability.Translation,
                LearningCapability.AiExplanations,
                LearningCapability.Vocabulary,
                LearningCapability.Reviews,
                LearningCapability.SentencePractice,
                LearningCapability.ScriptTrainer,
                LearningCapability.Progress,
                LearningCapability.PlayerTools,
                LearningCapability.ReaderTools),
            [LearningMode.Custom] = Build()
        };

    public static IReadOnlyDictionary<LearningCapability, bool> For(
        LearningMode mode) =>
        Defaults[mode];

    private static IReadOnlyDictionary<LearningCapability, bool> Build(
        params LearningCapability[] enabled)
    {
        var enabledSet = enabled.ToHashSet();
        return Enum.GetValues<LearningCapability>()
            .ToDictionary(
                capability => capability,
                enabledSet.Contains);
    }
}

public sealed class LearningConfigurationStore(AppDbContext db)
{
    public async Task<LearningMode> GetProfileModeAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        var snapshot = await GetScopeAsync(
            profileId,
            LearningScopeRef.Profile,
            cancellationToken);
        return snapshot.ModeOverride ?? LearningMode.Off;
    }

    public async Task<LearningScopeSettingsSnapshot> GetScopeAsync(
        string profileId,
        LearningScopeRef scope,
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
            LearningMode? mode = null;

            await using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    """
                    SELECT "ModeOverride"
                    FROM "LearningScopeModes"
                    WHERE "ProfileId" = $profileId
                      AND "ScopeType" = $scopeType
                      AND "ScopeKey" = $scopeKey
                    LIMIT 1;
                    """;
                Add(command, "$profileId", profileId);
                Add(command, "$scopeType", scope.Kind.ToString());
                Add(command, "$scopeKey", scope.Key);

                var value = await command.ExecuteScalarAsync(cancellationToken);
                if (value is string raw
                    && Enum.TryParse<LearningMode>(
                        raw,
                        ignoreCase: true,
                        out var parsed))
                {
                    mode = parsed;
                }
            }

            var capabilities = Enum.GetValues<LearningCapability>()
                .ToDictionary(x => x, _ => (bool?)null);

            await using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    """
                    SELECT "Capability", "IsEnabled"
                    FROM "LearningCapabilityOverrides"
                    WHERE "ProfileId" = $profileId
                      AND "ScopeType" = $scopeType
                      AND "ScopeKey" = $scopeKey;
                    """;
                Add(command, "$profileId", profileId);
                Add(command, "$scopeType", scope.Kind.ToString());
                Add(command, "$scopeKey", scope.Key);

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (Enum.TryParse<LearningCapability>(
                            reader.GetString(0),
                            ignoreCase: true,
                            out var capability))
                    {
                        capabilities[capability] = reader.GetInt32(1) != 0;
                    }
                }
            }

            return new LearningScopeSettingsSnapshot(
                scope,
                mode,
                capabilities);
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task SetModeAsync(
        string profileId,
        LearningScopeRef scope,
        LearningMode? mode,
        CancellationToken cancellationToken)
    {
        ValidateProfile(profileId);

        if (scope.Kind == LearningScopeKind.Profile && mode is null)
        {
            mode = LearningMode.Off;
        }

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            if (mode is null)
            {
                await using var delete = connection.CreateCommand();
                delete.CommandText =
                    """
                    DELETE FROM "LearningScopeModes"
                    WHERE "ProfileId" = $profileId
                      AND "ScopeType" = $scopeType
                      AND "ScopeKey" = $scopeKey;
                    """;
                Add(delete, "$profileId", profileId);
                Add(delete, "$scopeType", scope.Kind.ToString());
                Add(delete, "$scopeKey", scope.Key);
                await delete.ExecuteNonQueryAsync(cancellationToken);
                return;
            }

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO "LearningScopeModes" (
                    "ProfileId", "ScopeType", "ScopeKey", "ModeOverride", "UpdatedAt")
                VALUES (
                    $profileId, $scopeType, $scopeKey, $mode, $updatedAt)
                ON CONFLICT("ProfileId", "ScopeType", "ScopeKey") DO UPDATE SET
                    "ModeOverride" = excluded."ModeOverride",
                    "UpdatedAt" = excluded."UpdatedAt";
                """;
            Add(command, "$profileId", profileId);
            Add(command, "$scopeType", scope.Kind.ToString());
            Add(command, "$scopeKey", scope.Key);
            Add(command, "$mode", mode.Value.ToString());
            Add(command, "$updatedAt", DateTime.UtcNow);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task SetCapabilityOverrideAsync(
        string profileId,
        LearningScopeRef scope,
        LearningCapability capability,
        bool? enabled,
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
            if (enabled is null)
            {
                await using var delete = connection.CreateCommand();
                delete.CommandText =
                    """
                    DELETE FROM "LearningCapabilityOverrides"
                    WHERE "ProfileId" = $profileId
                      AND "ScopeType" = $scopeType
                      AND "ScopeKey" = $scopeKey
                      AND "Capability" = $capability;
                    """;
                Add(delete, "$profileId", profileId);
                Add(delete, "$scopeType", scope.Kind.ToString());
                Add(delete, "$scopeKey", scope.Key);
                Add(delete, "$capability", capability.ToString());
                await delete.ExecuteNonQueryAsync(cancellationToken);
                return;
            }

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO "LearningCapabilityOverrides" (
                    "ProfileId", "ScopeType", "ScopeKey",
                    "Capability", "IsEnabled", "UpdatedAt")
                VALUES (
                    $profileId, $scopeType, $scopeKey,
                    $capability, $isEnabled, $updatedAt)
                ON CONFLICT(
                    "ProfileId", "ScopeType", "ScopeKey", "Capability") DO UPDATE SET
                    "IsEnabled" = excluded."IsEnabled",
                    "UpdatedAt" = excluded."UpdatedAt";
                """;
            Add(command, "$profileId", profileId);
            Add(command, "$scopeType", scope.Kind.ToString());
            Add(command, "$scopeKey", scope.Key);
            Add(command, "$capability", capability.ToString());
            Add(command, "$isEnabled", enabled.Value ? 1 : 0);
            Add(command, "$updatedAt", DateTime.UtcNow);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task<LearningResolvedSettings> ResolveAsync(
        string profileId,
        LearningScopeContext context,
        CancellationToken cancellationToken)
    {
        ValidateProfile(profileId);
        var chain = context.BuildChain();

        var snapshots = new List<LearningScopeSettingsSnapshot>(chain.Count);
        foreach (var scope in chain)
        {
            snapshots.Add(await GetScopeAsync(
                profileId,
                scope,
                cancellationToken));
        }

        var mode = LearningMode.Off;
        foreach (var snapshot in snapshots)
        {
            if (snapshot.ModeOverride is { } modeOverride)
            {
                mode = modeOverride;
            }
        }

        var resolved = LearningConfigurationDefaults.For(mode)
            .ToDictionary(x => x.Key, x => x.Value);

        foreach (var snapshot in snapshots)
        {
            foreach (var capability in Enum.GetValues<LearningCapability>())
            {
                if (snapshot.GetOverride(capability) is { } value)
                {
                    resolved[capability] = value;
                }
            }
        }

        return new LearningResolvedSettings(mode, resolved);
    }

    public async Task<bool> HasAnyLearningEnabledAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        ValidateProfile(profileId);

        var profile = await GetScopeAsync(
            profileId,
            LearningScopeRef.Profile,
            cancellationToken);
        var profileMode = profile.ModeOverride ?? LearningMode.Off;

        if (profileMode != LearningMode.Off
            || profile.Capabilities.Any(x => x.Value == true))
        {
            return true;
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
                    EXISTS(
                        SELECT 1
                        FROM "LearningScopeModes"
                        WHERE "ProfileId" = $profileId
                          AND "ScopeType" <> 'Profile'
                          AND "ModeOverride" <> 'Off'
                    )
                    OR EXISTS(
                        SELECT 1
                        FROM "LearningCapabilityOverrides"
                        WHERE "ProfileId" = $profileId
                          AND "IsEnabled" = 1
                    );
                """;
            Add(command, "$profileId", profileId);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(value) != 0;
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static void ValidateProfile(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException("Profile ID is required.", nameof(profileId));
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
