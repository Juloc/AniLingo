using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.Monitoring;
using Jularr.Web.Features.Acquisition.Quality;

namespace Jularr.Web.Features.Acquisition.Release;

/// <summary>
/// Parses a release name into the shared <see cref="ReleaseInfo"/> shape. Most media types share
/// the scene/usenet grammar (<see cref="SceneReleaseParser"/>); a media type that needs different
/// behaviour supplies its own implementation through its <see cref="IMediaAcquisitionRegistration"/>.
/// </summary>
public interface IReleaseParser
{
    ReleaseInfo Parse(string releaseName);

    bool TryParse(string? releaseName, out ReleaseInfo release);
}

/// <summary>The default parser: the shared scene/usenet grammar in <see cref="ReleaseParser"/>.</summary>
public sealed class SceneReleaseParser : IReleaseParser
{
    public static SceneReleaseParser Instance { get; } = new();

    public ReleaseInfo Parse(string releaseName) => ReleaseParser.Parse(releaseName);

    public bool TryParse(string? releaseName, out ReleaseInfo release) =>
        ReleaseParser.TryParse(releaseName, out release);
}

/// <summary>
/// One media type's plug-in to the acquisition engine: how its release names are parsed and the
/// default quality profile new installs get for it. Anime is the first registration; Movie/TV/Book
/// register later without changing the engine.
/// </summary>
public interface IMediaAcquisitionRegistration
{
    MediaAcquisitionKind Kind { get; }

    IReleaseParser CreateReleaseParser();

    QualityProfile CreateDefaultQualityProfile();

    /// <summary>
    /// The granularity the shared monitoring engine tracks this media type at (see
    /// <see cref="MonitoringGranularity"/>). Episode is the default (anime/TV); whole-item media
    /// (movies, audiobooks, books) override it to <see cref="MonitoringGranularity.Item"/>. A default
    /// interface member so a registration that does not monitor need not implement it.
    /// </summary>
    MonitoringGranularity MonitoringGranularity => MonitoringGranularity.Episode;
}

/// <summary>
/// The media-type-agnostic entry point: resolves the parser and seed quality profile for any
/// registered media type. Fails fast on an unknown kind so a missing registration is obvious.
/// </summary>
public sealed class MediaAcquisitionRegistry
{
    private readonly Dictionary<MediaAcquisitionKind, IMediaAcquisitionRegistration> registrations;

    public MediaAcquisitionRegistry(IEnumerable<IMediaAcquisitionRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        var map = new Dictionary<MediaAcquisitionKind, IMediaAcquisitionRegistration>();
        foreach (var registration in registrations)
        {
            if (!map.TryAdd(registration.Kind, registration))
            {
                throw new InvalidOperationException(
                    $"Duplicate acquisition registration for media type '{registration.Kind}'.");
            }
        }

        this.registrations = map;
    }

    public IReadOnlyCollection<MediaAcquisitionKind> Kinds => registrations.Keys;

    public bool Supports(MediaAcquisitionKind kind) => registrations.ContainsKey(kind);

    public IReleaseParser ParserFor(MediaAcquisitionKind kind) => Get(kind).CreateReleaseParser();

    public QualityProfile DefaultProfileFor(MediaAcquisitionKind kind) => Get(kind).CreateDefaultQualityProfile();

    /// <summary>The granularity the monitoring engine tracks <paramref name="kind"/> at.</summary>
    public MonitoringGranularity MonitoringGranularityFor(MediaAcquisitionKind kind) => Get(kind).MonitoringGranularity;

    private IMediaAcquisitionRegistration Get(MediaAcquisitionKind kind) =>
        registrations.TryGetValue(kind, out var registration)
            ? registration
            : throw new InvalidOperationException(
                $"No acquisition registration for media type '{kind}'.");
}

/// <summary>Anime as one registration on the shared engine: scene parser + the 1080p anime profile.</summary>
public sealed class AnimeAcquisitionRegistration : IMediaAcquisitionRegistration
{
    public MediaAcquisitionKind Kind => MediaAcquisitionKind.Anime;

    public IReleaseParser CreateReleaseParser() => SceneReleaseParser.Instance;

    public QualityProfile CreateDefaultQualityProfile() => AnimeQualityProfiles.CreateDefaultAnime1080p();
}
