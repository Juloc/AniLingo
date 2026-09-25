using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace AniLingo.Web.Features.Library;

public sealed record NfoProviderIds(
    string? AniList,
    string? MyAnimeList,
    string? Tvdb,
    string? Tmdb,
    string? Imdb);

public sealed record NfoShowMetadata(
    string? Title,
    string? OriginalTitle,
    string? Plot,
    int? Year,
    DateOnly? Premiered,
    NfoProviderIds ProviderIds);

public sealed record NfoEpisodeMetadata(
    string? Title,
    string? Plot,
    int? SeasonNumber,
    int? EpisodeNumber,
    DateOnly? Aired,
    NfoProviderIds ProviderIds)
{
    // Numbers the NFO states must agree with the file-name identity; absent numbers do not veto.
    public bool Describes(int seasonNumber, int episodeNumber) =>
        (SeasonNumber is null || SeasonNumber == seasonNumber) &&
        (EpisodeNumber is null || EpisodeNumber == episodeNumber);
}

public sealed record NfoReadResult<T>(T? Value, string? Warning)
    where T : class
{
    public static NfoReadResult<T> Rejected(string warning) => new(null, warning);
}

// Reads the documented Kodi/Jellyfin/Sonarr NFO subset only. DTDs and external resources are
// prohibited, files above MaxFileBytes are rejected unread and any XML error rejects the whole file.
public static partial class NfoReader
{
    public const long MaxFileBytes = 1024 * 1024;
    public const int MaxTitleLength = 300;

    private const string ShowElement = "tvshow";
    private const string EpisodeElement = "episodedetails";

    private static readonly XmlReaderSettings ReaderSettings = new()
    {
        ConformanceLevel = ConformanceLevel.Fragment,
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = MaxFileBytes,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,
        CloseInput = false
    };

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^tt\d{5,10}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImdbIdRegex();

    public static NfoReadResult<NfoShowMetadata> ReadShow(string path) =>
        Read(path, ParseShow);

    public static NfoReadResult<IReadOnlyList<NfoEpisodeMetadata>> ReadEpisodes(string path) =>
        Read(path, ParseEpisodes);

    public static NfoReadResult<NfoShowMetadata> ParseShow(Stream stream)
    {
        if (!TryReadRootElements(stream, out var roots, out var warning))
        {
            return NfoReadResult<NfoShowMetadata>.Rejected(warning);
        }

        var show = roots.FirstOrDefault(x => IsNamed(x, ShowElement));
        if (show is null)
        {
            return NfoReadResult<NfoShowMetadata>.Rejected(
                $"NFO has no <{ShowElement}> element.");
        }

        return new NfoReadResult<NfoShowMetadata>(
            new NfoShowMetadata(
                ReadTitle(show, "title"),
                ReadTitle(show, "originaltitle"),
                ReadText(show, "plot") ?? ReadText(show, "outline"),
                ReadYear(show),
                ReadDate(show, "premiered"),
                ReadProviderIds(show)),
            null);
    }

    // Kodi writes one <episodedetails> root per episode for multi-episode files.
    public static NfoReadResult<IReadOnlyList<NfoEpisodeMetadata>> ParseEpisodes(Stream stream)
    {
        if (!TryReadRootElements(stream, out var roots, out var warning))
        {
            return NfoReadResult<IReadOnlyList<NfoEpisodeMetadata>>.Rejected(warning);
        }

        var episodes = roots
            .Where(x => IsNamed(x, EpisodeElement))
            .Select(episode => new NfoEpisodeMetadata(
                ReadTitle(episode, "title"),
                ReadText(episode, "plot"),
                ReadNumber(episode, "season", 0, 999),
                ReadNumber(episode, "episode", 0, 9999),
                ReadDate(episode, "aired"),
                ReadProviderIds(episode)))
            .ToArray();

        if (episodes.Length == 0)
        {
            return NfoReadResult<IReadOnlyList<NfoEpisodeMetadata>>.Rejected(
                $"NFO has no <{EpisodeElement}> element.");
        }

        return new NfoReadResult<IReadOnlyList<NfoEpisodeMetadata>>(episodes, null);
    }

    private static NfoReadResult<T> Read<T>(
        string path,
        Func<Stream, NfoReadResult<T>> parse)
        where T : class
    {
        try
        {
            var file = new FileInfo(path);
            if (file.Length > MaxFileBytes)
            {
                return NfoReadResult<T>.Rejected(
                    $"NFO is larger than the {MaxFileBytes / 1024} KiB limit.");
            }

            using var stream = new FileStream(
                file.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return parse(stream);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return NfoReadResult<T>.Rejected(
                $"NFO could not be read: {exception.Message}");
        }
    }

    // The whole input is consumed so trailing garbage also rejects the file. Top-level text such as
    // the URL line Kodi appends to hybrid NFO files is allowed by fragment conformance and ignored.
    private static bool TryReadRootElements(
        Stream stream,
        out IReadOnlyList<XElement> roots,
        out string warning)
    {
        var elements = new List<XElement>();
        try
        {
            using var reader = XmlReader.Create(stream, ReaderSettings);
            reader.Read();
            while (!reader.EOF)
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    elements.Add((XElement)XNode.ReadFrom(reader));
                }
                else
                {
                    reader.Read();
                }
            }
        }
        catch (XmlException exception)
        {
            roots = [];
            warning = $"NFO is not well-formed or uses prohibited XML features: {exception.Message}";
            return false;
        }

        roots = elements;
        warning = "";
        return true;
    }

    // <uniqueid type="..."> wins over legacy per-provider tags; among duplicates default="true" wins,
    // then document order. Malformed values are ignored. The ambiguous legacy <id> tag is not read.
    private static NfoProviderIds ReadProviderIds(XElement root)
    {
        var uniqueIds = Children(root, "uniqueid")
            .Select(element => new
            {
                Provider = NormalizeProviderType(element.Attribute("type")?.Value),
                IsDefault = string.Equals(
                    element.Attribute("default")?.Value?.Trim(),
                    "true",
                    StringComparison.OrdinalIgnoreCase),
                element.Value
            })
            .Where(x => x.Provider is not null)
            .OrderByDescending(x => x.IsDefault)
            .ToArray();

        string? Resolve(
            string provider,
            Func<string, string?> normalize,
            params string[] legacyElements) =>
            uniqueIds
                .Where(x => x.Provider == provider)
                .Select(x => normalize(x.Value))
                .FirstOrDefault(x => x is not null)
            ?? legacyElements
                .SelectMany(name => Children(root, name))
                .Select(x => normalize(x.Value))
                .FirstOrDefault(x => x is not null);

        return new NfoProviderIds(
            Resolve("anilist", NormalizeNumericId, "anilistid"),
            Resolve("mal", NormalizeNumericId, "malid", "myanimelistid"),
            Resolve("tvdb", NormalizeNumericId, "tvdbid"),
            Resolve("tmdb", NormalizeNumericId, "tmdbid"),
            Resolve("imdb", NormalizeImdbId, "imdb_id", "imdbid"));
    }

    private static string? NormalizeProviderType(string? type) =>
        type?.Trim().ToLowerInvariant() switch
        {
            "anilist" => "anilist",
            "mal" or "myanimelist" => "mal",
            "tvdb" => "tvdb",
            "tmdb" => "tmdb",
            "imdb" => "imdb",
            _ => null
        };

    private static string? NormalizeNumericId(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length is > 0 and <= 10 &&
               trimmed.All(char.IsAsciiDigit) &&
               int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var id) &&
               id > 0
            ? id.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static string? NormalizeImdbId(string value)
    {
        var trimmed = value.Trim();
        return ImdbIdRegex().IsMatch(trimmed)
            ? trimmed.ToLowerInvariant()
            : null;
    }

    private static string? ReadTitle(XElement root, string name)
    {
        var text = ReadText(root, name);
        if (text is null)
        {
            return null;
        }

        var title = WhitespaceRegex().Replace(text, " ");
        return title.Length <= MaxTitleLength ? title : null;
    }

    private static string? ReadText(XElement root, string name)
    {
        var value = Children(root, name).FirstOrDefault()?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static int? ReadYear(XElement root) =>
        ReadNumber(root, "year", 1900, 2100);

    private static int? ReadNumber(XElement root, string name, int minimum, int maximum)
    {
        var text = ReadText(root, name);
        return text is not null &&
               int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) &&
               value >= minimum &&
               value <= maximum
            ? value
            : null;
    }

    private static DateOnly? ReadDate(XElement root, string name)
    {
        var text = ReadText(root, name);
        return text is not null &&
               DateOnly.TryParseExact(
                   text,
                   "yyyy-MM-dd",
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.None,
                   out var date)
            ? date
            : null;
    }

    private static IEnumerable<XElement> Children(XElement root, string name) =>
        root.Elements().Where(x => IsNamed(x, name));

    private static bool IsNamed(XElement element, string name) =>
        string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase);
}
