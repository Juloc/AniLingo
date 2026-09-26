using System.Text;
using AniLingo.Web.Features.Learning.Toolkits;

namespace AniLingo.Web.Features.Vocabulary;

public sealed record JapaneseTermCandidate(string Canonical, string Reading);

/// <summary>
/// Japanese tokenization/term extraction: MeCab morphological analysis kept to
/// lexical parts of speech, dictionary-form lemmas and hiragana readings. Also
/// implements <see cref="ITermExtractor"/> so it can back the Japanese language
/// toolkit; the explicit interface member adapts <see cref="JapaneseTermCandidate"/>
/// to the toolkit-neutral <see cref="TermCandidate"/> without changing this
/// type's own <see cref="Extract"/> contract used elsewhere.
/// </summary>
public sealed class JapaneseTermExtractor(IJapaneseMorphology morphology) : ITermExtractor
{
    private static readonly HashSet<string> IncludedPartsOfSpeech =
    [
        "名詞",
        "動詞",
        "形容詞",
        "副詞",
        "連体詞",
        "接続詞",
        "感動詞"
    ];

    public IReadOnlyList<JapaneseTermCandidate> Extract(string text)
    {
        var result = new List<JapaneseTermCandidate>();

        foreach (var token in morphology.Analyze(text))
        {
            if (!IncludedPartsOfSpeech.Contains(token.PartOfSpeech))
            {
                continue;
            }

            var canonical = token.Canonical.Normalize(NormalizationForm.FormKC).Trim();
            if (!ContainsJapanese(canonical))
            {
                continue;
            }

            var reading = ToHiragana(token.Reading.Normalize(NormalizationForm.FormKC).Trim());
            if (!ContainsJapanese(reading))
            {
                reading = "";
            }

            result.Add(new JapaneseTermCandidate(canonical, reading));
        }

        return result;
    }

    IReadOnlyList<TermCandidate> ITermExtractor.Extract(string text) =>
        Extract(text)
            .Select(x => new TermCandidate(
                x.Canonical,
                string.IsNullOrEmpty(x.Reading) ? null : x.Reading))
            .ToArray();

    internal static string ToHiragana(string value)
    {
        var chars = value.ToCharArray();

        for (var index = 0; index < chars.Length; index++)
        {
            if (chars[index] is >= 'ァ' and <= 'ヶ')
            {
                chars[index] = (char)(chars[index] - 0x60);
            }
        }

        return new string(chars);
    }

    private static bool ContainsJapanese(string value) =>
        value.Any(character =>
            character is >= '\u3040' and <= '\u30ff'
                or >= '\u3400' and <= '\u4dbf'
                or >= '\u4e00' and <= '\u9fff'
                or '々'
                or '〆'
                or 'ヶ');
}
