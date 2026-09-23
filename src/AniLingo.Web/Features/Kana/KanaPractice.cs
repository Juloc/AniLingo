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

    public static bool IsSuitableSentence(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        return trimmed.Length is >= 2 and <= 90
            && trimmed.Any(IsJapaneseCharacter)
            && !trimmed.Contains('\n')
            && !trimmed.Contains('\r');
    }

    public static bool IsJapaneseCharacter(char character) =>
        character is >= '\u3040' and <= '\u30ff'
            or >= '\u3400' and <= '\u4dbf'
            or >= '\u4e00' and <= '\u9fff'
            or '々'
            or '〆'
            or 'ヶ';
}
