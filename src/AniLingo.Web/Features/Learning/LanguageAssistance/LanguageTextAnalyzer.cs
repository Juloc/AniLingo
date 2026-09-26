using System.Text;
using System.Text.RegularExpressions;
using AniLingo.Web.Features.Learning.Courses;
using AniLingo.Web.Features.Vocabulary;

namespace AniLingo.Web.Features.Learning.LanguageAssistance;

/// <summary>A token of content text with its lexical lookup.</summary>
public sealed record LanguageTextToken(
    string Surface,
    string? Canonical,
    string? Reading,
    string? Meaning,
    string? MeaningLanguage,
    bool Interactive)
{
    public static LanguageTextToken Plain(string surface) =>
        new(surface, null, null, null, null, false);
}

/// <summary>
/// Splits content text into words through the language toolkit. The Japanese
/// toolkit uses the morphological analyzer and the bundled dictionary for
/// dictionary forms, readings and meanings; every other language uses Unicode
/// word boundaries without readings or dictionary meanings.
/// </summary>
public sealed partial class LanguageTextAnalyzer(
    IJapaneseMorphology morphology,
    JapaneseDictionary dictionary)
{
    private static readonly LearningLanguageToolkitRegistry Toolkits = new();

    public IReadOnlyList<LanguageTextToken> Analyze(string text, string languageTag)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        return IsJapanese(languageTag)
            ? AnalyzeJapanese(text)
            : AnalyzeGeneric(text);
    }

    /// <summary>Reading and meaning of a dictionary form, when the toolkit has a dictionary.</summary>
    public (string? Reading, string? Meaning) LookUp(string canonical, string languageTag)
    {
        if (!IsJapanese(languageTag))
        {
            return (null, null);
        }

        var entry = dictionary.Find(canonical);
        if (entry is not null)
        {
            return (NullIfSame(entry.Reading, canonical), entry.Meaning);
        }

        var analyzed = morphology.Analyze(canonical);
        if (analyzed.Count != 1)
        {
            return (null, null);
        }

        var reading = JapaneseTermExtractor.ToHiragana(analyzed[0].Reading);
        return (JapaneseScript.Contains(reading) ? NullIfSame(reading, canonical) : null, null);
    }

    public static bool IsJapanese(string languageTag) =>
        Toolkits.Get(languageTag) is JapaneseLearningLanguageToolkit;

    private IReadOnlyList<LanguageTextToken> AnalyzeJapanese(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormKC);
        var analyzed = morphology.Analyze(normalized);
        var tokens = new List<LanguageTextToken>(analyzed.Count + 2);
        var cursor = 0;

        foreach (var token in analyzed)
        {
            if (string.IsNullOrEmpty(token.Surface))
            {
                continue;
            }

            var index = normalized.IndexOf(token.Surface, cursor, StringComparison.Ordinal);
            if (index < 0)
            {
                break;
            }

            if (index > cursor)
            {
                tokens.Add(LanguageTextToken.Plain(normalized[cursor..index]));
            }

            tokens.Add(DescribeJapanese(token));
            cursor = index + token.Surface.Length;
        }

        if (cursor < normalized.Length)
        {
            tokens.Add(LanguageTextToken.Plain(normalized[cursor..]));
        }

        return tokens;
    }

    private LanguageTextToken DescribeJapanese(JapaneseMorphToken token)
    {
        if (!JapaneseScript.Contains(token.Surface) || token.PartOfSpeech == "記号")
        {
            return LanguageTextToken.Plain(token.Surface);
        }

        var canonical = token.Canonical.Normalize(NormalizationForm.FormKC).Trim();
        if (canonical.Length == 0)
        {
            canonical = token.Surface;
        }

        var entry = dictionary.Find(canonical);
        var reading = entry?.Reading;
        if (string.IsNullOrWhiteSpace(reading) && canonical == token.Surface)
        {
            var analyzedReading = JapaneseTermExtractor.ToHiragana(
                token.Reading.Normalize(NormalizationForm.FormKC).Trim());
            reading = JapaneseScript.Contains(analyzedReading) ? analyzedReading : null;
        }

        return new LanguageTextToken(
            token.Surface,
            canonical,
            NullIfSame(reading, canonical),
            entry?.Meaning,
            entry?.Language,
            Interactive: true);
    }

    private static IReadOnlyList<LanguageTextToken> AnalyzeGeneric(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormC);
        var tokens = new List<LanguageTextToken>();
        var cursor = 0;

        foreach (Match match in WordRegex().Matches(normalized))
        {
            if (match.Index > cursor)
            {
                tokens.Add(LanguageTextToken.Plain(normalized[cursor..match.Index]));
            }

            var word = match.Value;
            tokens.Add(word.Any(char.IsLetter)
                ? new LanguageTextToken(word, word, null, null, null, Interactive: true)
                : LanguageTextToken.Plain(word));
            cursor = match.Index + match.Length;
        }

        if (cursor < normalized.Length)
        {
            tokens.Add(LanguageTextToken.Plain(normalized[cursor..]));
        }

        return tokens;
    }

    private static string? NullIfSame(string? reading, string canonical) =>
        string.IsNullOrWhiteSpace(reading) || reading == canonical ? null : reading;

    [GeneratedRegex(@"[\p{L}\p{M}\p{Nd}]+(?:['’\-][\p{L}\p{M}\p{Nd}]+)*")]
    private static partial Regex WordRegex();
}

/// <summary>Japanese script detection shared by inspection and sentence practice.</summary>
public static class JapaneseScript
{
    public static bool IsCharacter(char character) =>
        character is >= '぀' and <= 'ヿ'
            or >= '㐀' and <= '䶿'
            or >= '一' and <= '鿿'
            or '々'
            or '〆'
            or 'ヶ';

    public static bool Contains(string? value) =>
        !string.IsNullOrEmpty(value) && value.Any(IsCharacter);
}
