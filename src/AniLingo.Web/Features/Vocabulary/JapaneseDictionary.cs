using AniLingo.Web.Features.Learning.Toolkits;

namespace AniLingo.Web.Features.Vocabulary;

public sealed record JapaneseDictionaryEntry(
    string Reading,
    string Meaning,
    string Language,
    bool Common);

/// <summary>
/// Bundled JMdict (German, with English fallback) lookup. Also implements
/// <see cref="IDictionaryLookup"/> so it can back the Japanese language
/// toolkit's <c>Dictionary</c> capability; the explicit interface member
/// adapts <see cref="JapaneseDictionaryEntry"/> to the toolkit-neutral
/// <see cref="DictionaryLookupEntry"/> without changing this type's own
/// <see cref="Find"/> contract used elsewhere.
/// </summary>
public sealed class JapaneseDictionary : IDictionaryLookup
{
    public const string DefaultDirectory = "/app/dictionary";

    private readonly Lazy<IReadOnlyDictionary<string, JapaneseDictionaryEntry>> entries;

    public JapaneseDictionary()
        : this(DefaultDirectory)
    {
    }

    public JapaneseDictionary(string directory)
    {
        entries = new Lazy<IReadOnlyDictionary<string, JapaneseDictionaryEntry>>(
            () => Load(directory),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public JapaneseDictionaryEntry? Find(string canonical) =>
        entries.Value.GetValueOrDefault(canonical);

    DictionaryLookupEntry? IDictionaryLookup.Find(string canonical)
    {
        var entry = Find(canonical);
        return entry is null
            ? null
            : new DictionaryLookupEntry(entry.Reading, entry.Meaning, entry.Language, entry.Common);
    }

    private static IReadOnlyDictionary<string, JapaneseDictionaryEntry> Load(string directory)
    {
        var germanPath = Path.Combine(directory, "jmdict-ger.tsv");
        var englishPath = Path.Combine(directory, "jmdict-eng-common.tsv");

        if (!File.Exists(germanPath))
        {
            throw new FileNotFoundException("German JMdict lookup is missing.", germanPath);
        }

        if (!File.Exists(englishPath))
        {
            throw new FileNotFoundException("English JMdict fallback lookup is missing.", englishPath);
        }

        var result = new Dictionary<string, JapaneseDictionaryEntry>(StringComparer.Ordinal);

        LoadFile(germanPath, "de", overwriteWithCommon: true, result);
        LoadFile(englishPath, "en", overwriteWithCommon: false, result);

        return result;
    }

    private static void LoadFile(
        string path,
        string language,
        bool overwriteWithCommon,
        Dictionary<string, JapaneseDictionaryEntry> target)
    {
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var columns = line.Split('\t', 4);
            if (columns.Length != 4)
            {
                continue;
            }

            var canonical = columns[0].Trim();
            var reading = columns[1].Trim();
            var common = columns[2] == "1";
            var meaning = columns[3].Trim();

            if (canonical.Length == 0 || meaning.Length == 0)
            {
                continue;
            }

            var candidate = new JapaneseDictionaryEntry(
                JapaneseTermExtractor.ToHiragana(reading),
                meaning,
                language,
                common);

            if (!target.TryGetValue(canonical, out var existing))
            {
                target[canonical] = candidate;
                continue;
            }

            if (existing.Language == language
                && overwriteWithCommon
                && !existing.Common
                && candidate.Common)
            {
                target[canonical] = candidate;
            }
        }
    }
}
