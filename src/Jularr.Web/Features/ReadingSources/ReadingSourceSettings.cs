using System.Text.Json;
using Jularr.Web.Features.Novels;

namespace Jularr.Web.Features.ReadingSources;

/// <summary>
/// One reading source. <see cref="Name"/> is the source's own brand name; its user-facing
/// description lives in the UI catalog under <see cref="DescriptionKey"/>.
/// </summary>
public sealed record ReadingSourceDefinition(
    string Key,
    string Name,
    bool SupportsDirectImport,
    int DefaultPriority)
{
    public string DescriptionKey => $"readingSources.source.{Key}.description";
}

public sealed record ReadingSourcePreference(
    bool Enabled,
    int Priority);

public sealed record ReadingSourceSettingsState(
    IReadOnlyDictionary<string, ReadingSourcePreference> Providers)
{
    public static ReadingSourceSettingsState Default { get; } =
        ReadingSourceCatalog.CreateDefaultSettings();

    public bool IsEnabled(string provider) =>
        Providers.TryGetValue(provider, out var preference)
            ? preference.Enabled
            : ReadingSourceCatalog.TryGet(provider, out var definition) &&
              ReadingSourceCatalog.DefaultPreference(definition).Enabled;

    public int PriorityFor(string provider) =>
        Providers.TryGetValue(provider, out var preference)
            ? preference.Priority
            : ReadingSourceCatalog.TryGet(provider, out var definition)
                ? definition.DefaultPriority
                : int.MaxValue;

    public ReadingSourcePreference Get(string provider)
    {
        if (Providers.TryGetValue(provider, out var preference))
        {
            return preference;
        }

        return ReadingSourceCatalog.TryGet(provider, out var definition)
            ? ReadingSourceCatalog.DefaultPreference(definition)
            : new ReadingSourcePreference(false, int.MaxValue);
    }
}

public static class ReadingSourceCatalog
{
    private static readonly ReadingSourceDefinition[] All =
    [
        new(
            NcodeNovelSourceProvider.ProviderKey,
            "Shōsetsuka ni Narō",
            SupportsDirectImport: true,
            DefaultPriority: 10),
        new(
            NovelAniListProvider.ProviderKey,
            "AniList",
            SupportsDirectImport: false,
            DefaultPriority: 20)
    ];

    public static IReadOnlyList<ReadingSourceDefinition> Definitions => All;

    public static bool TryGet(
        string? provider,
        out ReadingSourceDefinition definition)
    {
        definition = All.FirstOrDefault(
            item => item.Key.Equals(
                provider,
                StringComparison.OrdinalIgnoreCase))!;

        return definition is not null;
    }

    public static ReadingSourceDefinition GetRequired(string provider) =>
        TryGet(provider, out var definition)
            ? definition
            : throw new InvalidOperationException(
                $"Unknown reading source '{provider}'.");

    public static ReadingSourcePreference DefaultPreference(
        ReadingSourceDefinition definition) =>
        new(
            Enabled: true,
            Priority: definition.DefaultPriority);

    public static ReadingSourceSettingsState CreateDefaultSettings() =>
        new(
            All.ToDictionary(
                definition => definition.Key,
                DefaultPreference,
                StringComparer.OrdinalIgnoreCase));

    public static ReadingSourceSettingsState Normalize(
        IReadOnlyDictionary<string, ReadingSourcePreference>? providers,
        bool rejectInvalidPriority)
    {
        var normalized = new Dictionary<string, ReadingSourcePreference>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var definition in All)
        {
            var preference = providers is not null &&
                             providers.TryGetValue(
                                 definition.Key,
                                 out var configured)
                ? configured
                : DefaultPreference(definition);

            if (preference.Priority is < 1 or > 999)
            {
                if (rejectInvalidPriority)
                {
                    throw new InvalidDataException(
                        $"Priority for reading source '{definition.Key}' must be between 1 and 999.");
                }

                preference = preference with
                {
                    Priority = definition.DefaultPriority
                };
            }

            normalized[definition.Key] = preference;
        }

        return new ReadingSourceSettingsState(normalized);
    }
}

public sealed class ReadingSourceSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);

    public static ReadingSourceSettingsStore Default { get; } =
        new("/data");

    public ReadingSourceSettingsStore(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        path = Path.Combine(
            dataRoot,
            "integrations",
            "reading-sources.json");
    }

    public async Task<ReadingSourceSettingsState> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(path))
            {
                return ReadingSourceSettingsState.Default;
            }

            try
            {
                var json = await File.ReadAllTextAsync(
                    path,
                    cancellationToken);
                var persisted =
                    JsonSerializer.Deserialize<PersistedReadingSourceSettings>(
                        json,
                        JsonOptions);

                return ReadingSourceCatalog.Normalize(
                    persisted?.Providers,
                    rejectInvalidPriority: true);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "Reading source settings contain invalid JSON.",
                    exception);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAsync(
        ReadingSourceSettingsState settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var normalized = ReadingSourceCatalog.Normalize(
            settings.Providers,
            rejectInvalidPriority: true);

        await gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(path)!);

            var temporaryPath =
                $"{path}.tmp-{Guid.NewGuid():N}";
            try
            {
                var persisted = new PersistedReadingSourceSettings(
                    normalized.Providers.ToDictionary(
                        item => item.Key,
                        item => item.Value,
                        StringComparer.OrdinalIgnoreCase));

                await File.WriteAllTextAsync(
                    temporaryPath,
                    JsonSerializer.Serialize(
                        persisted,
                        JsonOptions),
                    cancellationToken);

                File.Move(
                    temporaryPath,
                    path,
                    overwrite: true);
            }
            finally
            {
                TryDelete(temporaryPath);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record PersistedReadingSourceSettings(
        Dictionary<string, ReadingSourcePreference>? Providers);
}
