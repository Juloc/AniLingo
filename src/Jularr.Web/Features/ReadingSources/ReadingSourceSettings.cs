using System.Text.Json;
using Jularr.Web.Features.Novels;
using Jularr.Web.Features.ReadingDiscovery;

namespace Jularr.Web.Features.ReadingSources;

/// <summary>
/// What a source can do for a result. Discovery and metadata sources never imply that
/// Jularr may fetch or copy full text: only <see cref="PublicFullText"/> (freely readable,
/// direct import) and <see cref="Acquisition"/> (the normal request and Usenet flow) make a
/// result addable to the library.
/// </summary>
[Flags]
public enum ReadingSourceCapabilities
{
    None = 0,
    /// <summary>Catalog and bibliographic metadata.</summary>
    Metadata = 1,
    /// <summary>Official sample or trial pages on the source. Linked to, never imported.</summary>
    Preview = 2,
    /// <summary>Freely readable text that may be imported directly.</summary>
    PublicFullText = 4,
    /// <summary>Results open on the source's own site.</summary>
    ExternalReference = 8,
    /// <summary>Published editions acquired through the request and Usenet flow.</summary>
    Acquisition = 16
}

/// <summary>
/// One reading source. <see cref="Name"/> is the source's own brand name; its user-facing
/// description and licensing note live in the UI catalog under <see cref="DescriptionKey"/>
/// and <see cref="LicensingKey"/>.
/// </summary>
public sealed record ReadingSourceDefinition(
    string Key,
    string Name,
    ReadingSourceCapabilities Capabilities,
    int DefaultPriority,
    bool EnabledByDefault = true,
    Func<string, bool>? ExternalIdValidator = null,
    Func<string, string>? DirectImportUrl = null)
{
    public string DescriptionKey => $"readingSources.source.{Key}.description";

    public string LicensingKey => $"readingSources.source.{Key}.licensing";

    public bool SupportsDirectImport =>
        Capabilities.HasFlag(ReadingSourceCapabilities.PublicFullText);

    /// <summary>
    /// Whether a result of this source can be added (imported or requested). Reference and
    /// preview sources are discovery only; the server rejects adding their results.
    /// </summary>
    public bool CanAdd =>
        (Capabilities & (ReadingSourceCapabilities.PublicFullText |
                         ReadingSourceCapabilities.Acquisition)) != 0;

    /// <summary>The single capability that labels this source's results.</summary>
    public ReadingSourceCapabilities PrimaryCapability =>
        Capabilities.HasFlag(ReadingSourceCapabilities.PublicFullText)
            ? ReadingSourceCapabilities.PublicFullText
            : Capabilities.HasFlag(ReadingSourceCapabilities.Acquisition)
                ? ReadingSourceCapabilities.Acquisition
                : Capabilities.HasFlag(ReadingSourceCapabilities.Preview)
                    ? ReadingSourceCapabilities.Preview
                    : Capabilities.HasFlag(ReadingSourceCapabilities.ExternalReference)
                        ? ReadingSourceCapabilities.ExternalReference
                        : ReadingSourceCapabilities.Metadata;

    public string CapabilityKey => CapabilityKeyFor(PrimaryCapability);

    public bool IsValidExternalId(string? externalId) =>
        !string.IsNullOrWhiteSpace(externalId) &&
        (ExternalIdValidator?.Invoke(externalId.Trim()) ?? true);

    public static string CapabilityKeyFor(ReadingSourceCapabilities capability) =>
        capability switch
        {
            ReadingSourceCapabilities.PublicFullText => "readingSources.capability.publicFullText",
            ReadingSourceCapabilities.Acquisition => "readingSources.capability.acquisition",
            ReadingSourceCapabilities.Preview => "readingSources.capability.preview",
            ReadingSourceCapabilities.ExternalReference => "readingSources.capability.externalReference",
            _ => "readingSources.capability.metadata"
        };
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
    // Priority is the default order among equally relevant results: sources that can be added
    // come first, discovery-only sources after them. Only sources with a supported access
    // path are listed; Jularr never scrapes sites that block automated access.
    private static readonly ReadingSourceDefinition[] All =
    [
        new(
            NcodeNovelSourceProvider.ProviderKey,
            "Shōsetsuka ni Narō",
            ReadingSourceCapabilities.Metadata | ReadingSourceCapabilities.PublicFullText,
            DefaultPriority: 10,
            ExternalIdValidator: SyosetuCatalogClient.IsValidNcode,
            DirectImportUrl: ncode => $"https://ncode.syosetu.com/{ncode.ToLowerInvariant()}/"),
        new(
            NovelAniListProvider.ProviderKey,
            "AniList",
            ReadingSourceCapabilities.Metadata | ReadingSourceCapabilities.Acquisition,
            DefaultPriority: 20,
            ExternalIdValidator: id => int.TryParse(id, out var aniListId) && aniListId > 0),
        new(
            BookWalkerCatalogProvider.ProviderKey,
            "BOOK☆WALKER",
            ReadingSourceCapabilities.Metadata |
            ReadingSourceCapabilities.Preview |
            ReadingSourceCapabilities.ExternalReference,
            DefaultPriority: 30),
        new(
            WebNovelCatalogProvider.ProviderKey,
            "WebNovel",
            ReadingSourceCapabilities.Metadata | ReadingSourceCapabilities.ExternalReference,
            DefaultPriority: 40,
            // WebNovel puts its pages behind a bot challenge; automated access is opt-in.
            EnabledByDefault: false),
        new(
            InternetArchiveCatalogProvider.ProviderKey,
            "Internet Archive",
            ReadingSourceCapabilities.Metadata | ReadingSourceCapabilities.ExternalReference,
            DefaultPriority: 50)
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
            Enabled: definition.EnabledByDefault,
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
