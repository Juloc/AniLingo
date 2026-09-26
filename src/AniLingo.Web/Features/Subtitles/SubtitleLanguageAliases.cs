namespace AniLingo.Web.Features.Subtitles;

/// <summary>
/// Maps a BCP-47 content-language tag to the tokens that identify it in file
/// names and embedded-track language tags: ISO 639-1/639-2 codes and common
/// English/native names. This is the single alias table subtitle acquisition
/// uses to decide "does this file name or stream tag mean language X" -
/// <see cref="AniLingo.Web.Features.Library.SubtitleSidecarLocator"/> and
/// <see cref="EmbeddedSubtitleExtractor"/> both call it instead of keeping
/// their own token lists.
/// </summary>
/// <remarks>
/// Extend by adding an entry. A language without one is still matched by its
/// bare BCP-47 tag alone (e.g. a target of <c>fr-CA</c> still matches a file
/// or stream tagged exactly <c>fr-CA</c>); the table only adds *extra* tokens
/// such as the ISO 639-2 code or the language's common name.
/// </remarks>
public static class SubtitleLanguageAliases
{
    private static readonly IReadOnlyDictionary<string, string[]> Aliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["ja"] = ["jp", "jpn", "japanese", "日本語"],
            ["de"] = ["deu", "ger", "german", "deutsch"],
            ["id"] = ["ind", "indonesian"],
            ["en"] = ["eng", "english"],
            ["ro"] = ["ron", "rum", "romanian"],
            ["fr"] = ["fra", "fre", "french"],
            ["es"] = ["spa", "spanish"],
            ["it"] = ["ita", "italian"],
            ["pt"] = ["por", "portuguese"],
            ["ru"] = ["rus", "russian"],
            ["zh"] = ["chi", "zho", "chinese", "chs", "cht"],
            ["ko"] = ["kor", "korean"],
            ["ar"] = ["ara", "arabic"],
            ["nl"] = ["dut", "nld", "dutch"],
            ["pl"] = ["pol", "polish"],
            ["tr"] = ["tur", "turkish"],
            ["vi"] = ["vie", "vietnamese"],
            ["th"] = ["tha", "thai"],
            ["ms"] = ["may", "msa", "malay"]
        };

    /// <summary>Every token (the tag itself plus any aliases) that names <paramref name="languageTag"/>.</summary>
    public static IReadOnlyCollection<string> TokensFor(string languageTag)
    {
        var tag = languageTag.Trim();
        return Aliases.TryGetValue(tag, out var extra)
            ? [tag, .. extra]
            : [tag];
    }

    /// <summary>
    /// The subset of <see cref="TokensFor"/> safe to look for as a free-form
    /// substring of a stream/file title: short ISO codes are excluded because
    /// they collide with ordinary words (English "eng" is fine as an exact
    /// file-name token but "ja" inside arbitrary title text is not evidence of
    /// anything). Names of at least four characters, and any non-ASCII
    /// script (kanji/kana names are unambiguous regardless of length), are
    /// kept.
    /// </summary>
    public static IReadOnlyCollection<string> NameTokensFor(string languageTag) =>
        TokensFor(languageTag)
            .Where(token => token.Length >= 4 || token.Any(ch => ch > 127))
            .ToArray();

    /// <summary>Every token known to name some language, across every entry in the table.</summary>
    public static IReadOnlyCollection<string> AllKnownTokens { get; } =
        Aliases
            .SelectMany(entry => entry.Value.Append(entry.Key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool HasToken(IEnumerable<string> tokens, string languageTag)
    {
        var own = TokensFor(languageTag);
        return tokens.Any(token => own.Contains(token, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// True when one of <paramref name="tokens"/> is a known token of some
    /// *other* language than <paramref name="languageTag"/>. Used to reject an
    /// untagged-content heuristic ever claiming a file that explicitly names a
    /// different language.
    /// </summary>
    public static bool IsTaggedAsOtherLanguage(IEnumerable<string> tokens, string languageTag)
    {
        var own = TokensFor(languageTag).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return tokens.Any(token => AllKnownTokens.Contains(token) && !own.Contains(token));
    }
}
