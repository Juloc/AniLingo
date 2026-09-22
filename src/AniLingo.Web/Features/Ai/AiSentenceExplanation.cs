using System.Text;
using System.Text.RegularExpressions;

namespace AniLingo.Web.Features.Ai;

public sealed record AiSentenceExplanation(
    string Translation,
    IReadOnlyList<string> Grammar,
    IReadOnlyList<string> Colloquial,
    bool FromCache);

public sealed record AiSentenceExplainRequest(string Sentence);

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

    private static readonly (string Needle, string Hint)[] LiteralRules =
    [
        ("なければならない", "なければならない = müssen"),
        ("なければいけない", "なければいけない = müssen"),
        ("なくてはいけない", "なくてはいけない = müssen"),
        ("なくてもいい", "なくてもいい = muss nicht / darf ohne"),
        ("てはいけない", "てはいけない = darf nicht"),
        ("ちゃいけない", "ちゃいけない→てはいけない"),
        ("ことにする", "ことにする = sich entscheiden, etwas zu tun"),
        ("ことになる", "ことになる = es wird entschieden / dazu kommen"),
        ("ことがある", "ことがある = Erfahrung / etwas kommt vor"),
        ("ようになる", "ようになる = Veränderung: dazu kommen, etwas zu tun/können"),
        ("ようにする", "ようにする = sich bemühen / dafür sorgen"),
        ("かもしれない", "かもしれない = vielleicht / möglicherweise"),
        ("と思う", "と思う = denken, dass … / Zitat + denken"),
        ("ほうがいい", "ほうがいい = Empfehlung: besser …"),
        ("てほしい", "てほしい = wollen, dass jemand etwas tut"),
        ("てみる", "てみる = versuchsweise etwas tun"),
        ("ておく", "ておく = im Voraus / vorsorglich tun"),
        ("てしまう", "てしまう = vollständig/versehentlich tun; oft Bedauern"),
        ("てくれる", "てくれる = jemand tut etwas für mich/uns"),
        ("てもらう", "てもらう = etwas von jemandem getan bekommen"),
        ("てあげる", "てあげる = etwas für jemand anderen tun"),
        ("けれど", "けれど = aber / obwohl"),
        ("けど", "けど = aber / obwohl"),
        ("ので", "ので = weil / da"),
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

    private static readonly (Regex Pattern, string Hint)[] RegexRules =
    [
        (ShikaNaiRegex(), "しか…ない = nur / nichts außer"),
        (TariTariRegex(), "たり…たりする = Beispiele von Handlungen aufzählen")
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

        var hints = LiteralRules
            .Where(rule => normalized.Contains(rule.Needle, StringComparison.Ordinal))
            .Select(rule => rule.Hint)
            .Concat(
                RegexRules
                    .Where(rule => rule.Pattern.IsMatch(normalized))
                    .Select(rule => rule.Hint))
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToArray();

        return new PreparedJapaneseSentence(normalized, hints);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"しか[^。！？!?]{0,48}ない")]
    private static partial Regex ShikaNaiRegex();

    [GeneratedRegex(@"たり[^。！？!?]{0,48}たり(?:する|して|した|します|しない)?")]
    private static partial Regex TariTariRegex();
}
