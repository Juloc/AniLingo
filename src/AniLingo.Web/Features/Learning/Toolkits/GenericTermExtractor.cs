using System.Text;
using System.Text.RegularExpressions;

namespace AniLingo.Web.Features.Learning.Toolkits;

/// <summary>
/// Tokenizes any language by Unicode word boundaries with case folding. This is
/// what every language without a specialized toolkit uses (Japanese keeps its
/// own MeCab-based extractor instead). It never produces a reading and never
/// invents a dictionary meaning; a small, explicitly-curated stopword list
/// keeps the most common function words out of the catalog for the few
/// languages listed in <see cref="GenericStopwords"/>, and every other
/// language gets no stopword filtering at all rather than a guessed one.
/// </summary>
public sealed partial class GenericTermExtractor : ITermExtractor
{
    private readonly IReadOnlySet<string> stopwords;

    public GenericTermExtractor(string languageTag)
    {
        var primary = string.IsNullOrWhiteSpace(languageTag)
            ? ""
            : languageTag.Split('-')[0].ToLowerInvariant();
        stopwords = GenericStopwords.For(primary);
    }

    public IReadOnlyList<TermCandidate> Extract(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var normalized = text.Normalize(NormalizationForm.FormC);
        var result = new List<TermCandidate>();

        foreach (Match match in WordRegex().Matches(normalized))
        {
            var word = match.Value;
            if (!word.Any(char.IsLetter))
            {
                continue;
            }

            var folded = word.ToLowerInvariant();
            if (stopwords.Contains(folded))
            {
                continue;
            }

            result.Add(new TermCandidate(folded));
        }

        return result;
    }

    [GeneratedRegex(@"[\p{L}\p{M}\p{Nd}]+(?:['’\-][\p{L}\p{M}\p{Nd}]+)*")]
    private static partial Regex WordRegex();
}

/// <summary>
/// Small, explicitly-added stopword sets for a few languages. This is not a
/// language-detection mechanism and it never grows into a giant list: a
/// language only gets filtering here when someone explicitly adds a minimal
/// set for it. Every other language tag simply gets an empty set, i.e. no
/// stopword filtering.
/// </summary>
public static class GenericStopwords
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> ByLanguage =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["de"] = Set(
                "der", "die", "das", "und", "ist", "ich", "du", "es", "ein", "eine",
                "zu", "in", "den", "dem", "nicht", "mit", "auf", "für", "von", "sich"),
            ["id"] = Set(
                "yang", "dan", "di", "ke", "dari", "itu", "ini", "untuk", "dengan",
                "tidak", "ada", "adalah", "akan", "atau", "juga", "saya", "kamu"),
            ["ro"] = Set(
                "și", "de", "la", "în", "este", "un", "o", "pe", "cu", "nu", "ce",
                "să", "se", "din", "că", "pentru", "mai"),
            ["en"] = Set(
                "the", "a", "an", "and", "is", "to", "of", "in", "it", "that",
                "this", "for", "on", "with", "as", "at", "by", "or")
        };

    private static readonly IReadOnlySet<string> Empty = new HashSet<string>();

    public static IReadOnlySet<string> For(string primaryLanguageTag) =>
        !string.IsNullOrEmpty(primaryLanguageTag) && ByLanguage.TryGetValue(primaryLanguageTag, out var set)
            ? set
            : Empty;

    private static IReadOnlySet<string> Set(params string[] words) =>
        new HashSet<string>(words, StringComparer.Ordinal);
}
