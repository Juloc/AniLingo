using System.Text;
using System.Text.RegularExpressions;

namespace AniLingo.Web.Features.Vocabulary;

public sealed partial class JapaneseTermExtractor
{
    private static readonly string[] TrailingParticles = ["から", "まで", "より", "は", "が", "を", "に", "で", "と", "へ", "も", "の"];

    [GeneratedRegex(@"[一-龯々〆ヵヶ]+[ぁ-ゖー]*|[ァ-ヺー]{2,}|[ぁ-ゖー]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex CandidateRegex();

    public IReadOnlyList<string> Extract(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormKC);
        var result = new List<string>();

        foreach (Match match in CandidateRegex().Matches(normalized))
        {
            var candidate = match.Value.Trim();
            if (candidate.Length == 0)
            {
                continue;
            }

            foreach (var term in SplitTrailingParticle(candidate))
            {
                if (IsUseful(term))
                {
                    result.Add(term);
                }
            }
        }

        return result;
    }

    private static IEnumerable<string> SplitTrailingParticle(string candidate)
    {
        foreach (var particle in TrailingParticles)
        {
            if (candidate.Length > particle.Length &&
                candidate.EndsWith(particle, StringComparison.Ordinal) &&
                ContainsKanji(candidate))
            {
                yield return candidate[..^particle.Length];
                yield break;
            }
        }

        yield return candidate;
    }

    private static bool IsUseful(string value) =>
        value.Length >= 2 || ContainsKanji(value);

    private static bool ContainsKanji(string value) =>
        value.Any(ch => ch is >= '\u4e00' and <= '\u9fff' or '々' or '〆');
}
