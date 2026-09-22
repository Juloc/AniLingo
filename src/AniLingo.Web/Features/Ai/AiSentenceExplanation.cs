using System.Text;
using System.Text.RegularExpressions;

namespace AniLingo.Web.Features.Ai;

public sealed record AiSentenceExplanation(
    string Translation,
    IReadOnlyList<string> Grammar,
    IReadOnlyList<string> Colloquial,
    bool FromCache);

public sealed record AiSentenceExplainRequest(
    string Sentence,
    IReadOnlyList<string> LocalHints);

public interface IAiSentenceExplainer
{
    string Id { get; }

    Task<AiSentenceExplanation> ExplainSentenceAsync(
        AiSentenceExplainRequest request,
        CancellationToken cancellationToken);
}

public sealed class AiSentenceExplanationCache
{
    public string CacheKey { get; set; } = "";
    public string ProviderId { get; set; } = "";
    public int PromptVersion { get; set; }
    public string Translation { get; set; } = "";
    public string GrammarJson { get; set; } = "[]";
    public string ColloquialJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed record PreparedJapaneseSentence(
    string Sentence,
    IReadOnlyList<string> LocalHints);

public static partial class JapaneseSentencePreprocessor
{
    private const int MaxSentenceLength = 500;

    private static readonly (string Needle, string Hint)[] Patterns =
    [
        ("なくちゃ", "なくちゃ→なくては"),
        ("なきゃ", "なきゃ→なければ"),
        ("ちゃう", "ちゃう→てしまう"),
        ("じゃう", "じゃう→でしまう"),
        ("てない", "てない→ていない"),
        ("てた", "てた→ていた"),
        ("てる", "てる→ている"),
        ("じゃん", "じゃん→じゃないか"),
        ("んです", "んです→のです"),
        ("んだ", "んだ→のだ")
    ];

    public static PreparedJapaneseSentence Prepare(string sentence)
    {
        var normalized = WhitespaceRegex()
            .Replace(sentence.Normalize(NormalizationForm.FormKC), " ")
            .Trim();

        if (normalized.Length > MaxSentenceLength)
        {
            normalized = normalized[..MaxSentenceLength];
        }

        var hints = Patterns
            .Where(pattern => normalized.Contains(pattern.Needle, StringComparison.Ordinal))
            .Select(pattern => pattern.Hint)
            .Distinct(StringComparer.Ordinal)
            .Take(4)
            .ToArray();

        return new PreparedJapaneseSentence(normalized, hints);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
