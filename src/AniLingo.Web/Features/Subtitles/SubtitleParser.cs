using System.Globalization;
using System.Text.RegularExpressions;

namespace AniLingo.Web.Features.Subtitles;

public static partial class SubtitleParser
{
    [GeneratedRegex(@"(?<h>\d{1,2}):(?<m>\d{2}):(?<s>\d{2})[,.](?<ms>\d{2,3})", RegexOptions.CultureInvariant)]
    private static partial Regex TimestampRegex();

    [GeneratedRegex(@"\{[^}]*\}|<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex FormattingRegex();

    public static IReadOnlyList<SubtitleCueData> Parse(string path, string content) =>
        ParseFormat(Path.GetExtension(path), content);

    public static IReadOnlyList<SubtitleCueData> ParseFormat(string format, string content) =>
        format.Trim().TrimStart('.').ToLowerInvariant() switch
        {
            "srt" => ParseSrt(content),
            "ass" or "ssa" => ParseAss(content),
            _ => throw new NotSupportedException($"Unsupported subtitle format: {format}")
        };

    public static IReadOnlyList<SubtitleCueData> ParseSrt(string content)
    {
        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');
        var blocks = Regex.Split(normalized.Trim(), @"\n{2,}");
        var result = new List<SubtitleCueData>(blocks.Length);

        foreach (var block in blocks)
        {
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var timingIndex = Array.FindIndex(lines, line => line.Contains("-->", StringComparison.Ordinal));
            if (timingIndex < 0)
            {
                continue;
            }

            var timing = lines[timingIndex].Split("-->", StringSplitOptions.TrimEntries);
            if (timing.Length != 2 ||
                !TryParseTimestamp(timing[0], out var start) ||
                !TryParseTimestamp(timing[1], out var end))
            {
                continue;
            }

            var text = string.Join(" ", lines.Skip(timingIndex + 1));
            text = CleanText(text);
            if (text.Length > 0)
            {
                result.Add(new SubtitleCueData(start, end, text));
            }
        }

        return result;
    }

    public static IReadOnlyList<SubtitleCueData> ParseAss(string content)
    {
        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');
        var result = new List<SubtitleCueData>();
        string[]? format = null;
        var inEvents = false;

        foreach (var rawLine in normalized.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("[", StringComparison.Ordinal))
            {
                inEvents = line.Equals("[Events]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inEvents)
            {
                continue;
            }

            if (line.StartsWith("Format:", StringComparison.OrdinalIgnoreCase))
            {
                format = line["Format:".Length..]
                    .Split(',', StringSplitOptions.TrimEntries);
                continue;
            }

            if (format is null || !line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var values = line["Dialogue:".Length..].Split(',', format.Length, StringSplitOptions.None);
            if (values.Length != format.Length)
            {
                continue;
            }

            var startIndex = Array.FindIndex(format, x => x.Equals("Start", StringComparison.OrdinalIgnoreCase));
            var endIndex = Array.FindIndex(format, x => x.Equals("End", StringComparison.OrdinalIgnoreCase));
            var textIndex = Array.FindIndex(format, x => x.Equals("Text", StringComparison.OrdinalIgnoreCase));
            if (startIndex < 0 || endIndex < 0 || textIndex < 0 ||
                !TryParseTimestamp(values[startIndex], out var start) ||
                !TryParseTimestamp(values[endIndex], out var end))
            {
                continue;
            }

            var text = CleanText(values[textIndex].Replace("\\N", " ", StringComparison.OrdinalIgnoreCase));
            if (text.Length > 0)
            {
                result.Add(new SubtitleCueData(start, end, text));
            }
        }

        return result;
    }

    private static bool TryParseTimestamp(string value, out int milliseconds)
    {
        milliseconds = 0;
        var match = TimestampRegex().Match(value.Trim());
        if (!match.Success)
        {
            return false;
        }

        var hours = int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture);
        var minutes = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
        var seconds = int.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture);
        var fraction = match.Groups["ms"].Value;
        var ms = fraction.Length == 2
            ? int.Parse(fraction, CultureInfo.InvariantCulture) * 10
            : int.Parse(fraction, CultureInfo.InvariantCulture);

        milliseconds = (int)TimeSpan.FromHours(hours).TotalMilliseconds
            + (int)TimeSpan.FromMinutes(minutes).TotalMilliseconds
            + (seconds * 1000)
            + ms;

        return true;
    }

    private static string CleanText(string value) =>
        Regex.Replace(FormattingRegex().Replace(value, ""), @"\s+", " ").Trim();
}
