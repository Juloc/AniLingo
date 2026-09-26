namespace AniLingo.Web.Features.Kana;

public enum KanaPracticeMode
{
    KanaToRomaji,
    RomajiToKana,
    AudioToKana
}

public static class KanaPractice
{
    public static bool TryParseMode(string? value, out KanaPracticeMode mode)
    {
        mode = value?.ToLowerInvariant() switch
        {
            "kana-to-romaji" => KanaPracticeMode.KanaToRomaji,
            "romaji-to-kana" => KanaPracticeMode.RomajiToKana,
            "audio-to-kana" => KanaPracticeMode.AudioToKana,
            _ => (KanaPracticeMode)(-1)
        };

        return Enum.IsDefined(mode);
    }

    public static string Expected(KanaEntry entry, KanaPracticeMode mode) =>
        mode == KanaPracticeMode.KanaToRomaji ? entry.Romaji : entry.Symbol;

    public static bool IsCorrect(KanaEntry entry, KanaPracticeMode mode, string? answer)
    {
        var expected = Expected(entry, mode);
        var actual = answer?.Trim() ?? "";
        return mode == KanaPracticeMode.KanaToRomaji
            ? string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
            : string.Equals(actual, expected, StringComparison.Ordinal);
    }
}
