using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jularr.Web.Features.Naming;

/// <summary>
/// Small generic <c>{token}</c> template renderer: word-separator substitution, case-by-token-
/// casing, zero-padding for numeric tokens, illegal-character replacement, repeated-separator
/// collapsing and reserved device-name suffixing. Token *values* are resolved by the caller
/// (<see cref="TokenResolver"/>); this class only owns the generic template grammar and filename-
/// safety rules.
///
/// Extracted for the reading naming profiles (#529, Books/Manga/Light Novels). It is not a
/// generic-ized version of <c>Features/Acquisition/Naming/AnimeNamingFormatter</c>: that
/// formatter is tightly coupled to Sonarr-style season/episode/quality/media-info concepts (and
/// Sonarr's per-character illegal-character table and colon-replacement strategies) and is not a
/// reusable engine on its own. Rather than duplicate its token grammar and safety rules wholesale,
/// this factors out the part that genuinely is generic; illegal characters are replaced with "_"
/// here (matching the blanket sanitizing <c>MangaLibraryPlacement.SafeName</c>/
/// <c>ReadingLibraryPlacement</c> already did before naming profiles existed) rather than
/// reproducing Sonarr's table, so the built-in default profile renders byte-identical folder/file
/// names to Jularr's pre-#529 placement. It is not retrofitted into the anime formatter here to
/// stay out of that file's own claimed scope.
/// </summary>
public static partial class NamingTemplateEngine
{
    public const int MaxNameBytes = 255;

    private const string IllegalLiteralCharacters = "\\/:*?\"<>|";

    private static readonly HashSet<char> InvalidNameCharacters = Path.GetInvalidFileNameChars()
        .Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|'])
        .ToHashSet();

    [GeneratedRegex(
        @"\{(?<token>[a-z0-9]+(?:(?<separator>[- ._]+)[a-z0-9]+)?)(?::(?<pad>0+))?\}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"([- ._])\1+", RegexOptions.CultureInvariant)]
    private static partial Regex RepeatedSeparatorRegex();

    [GeneratedRegex(@"[- ._]+$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingSeparatorRegex();

    [GeneratedRegex(@"^(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReservedDeviceNameRegex();

    /// <summary>Resolves one token to its rendered value; null means the token is unknown or not
    /// available in the current scope (the caller should record an error), empty string means the
    /// token is known but has no value here (renders as nothing, like anime naming).</summary>
    public delegate string? TokenResolver(string key, int padZeros, List<string>? errors);

    public static bool ExceedsNameLimit(string name) =>
        Encoding.UTF8.GetByteCount(name) > MaxNameBytes;

    public static string Render(
        string template,
        TokenResolver resolveToken,
        List<string>? errors = null)
    {
        ArgumentNullException.ThrowIfNull(resolveToken);

        var result = TokenRegex().Replace(
            template ?? "",
            match => RenderToken(match, resolveToken, errors));

        result = RepeatedSeparatorRegex().Replace(result, "$1");
        result = TrailingSeparatorRegex().Replace(result, "");
        result = result.Trim(' ', '.', '_');
        return result.Length == 0
            ? result
            : ReservedDeviceNameRegex().Replace(result, match => $"{match.Groups[1].Value}_");
    }

    // Blanket-sanitizes characters that are illegal (or merely risky on some filesystems/SMB
    // shares) in file names to "_", matching MangaLibraryPlacement.SafeName/ReadingLibraryPlacement
    // so the default naming profile renders exactly what Jularr already wrote to disk before
    // naming profiles existed.
    public static string CleanFileName(string value)
    {
        var cleaned = new string((value ?? "")
                .Select(character => InvalidNameCharacters.Contains(character) || char.IsControl(character) ? '_' : character)
                .ToArray())
            .Trim(' ', '.', '_');
        return cleaned;
    }

    /// <summary>Validates literal (non-token) template text for illegal characters and malformed braces.</summary>
    public static IReadOnlyList<string> ValidateLiteral(string label, string? template)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(template))
        {
            errors.Add($"{label} is required.");
            return errors;
        }

        if (template.Length > 500)
        {
            errors.Add($"{label} must be at most 500 characters.");
            return errors;
        }

        var literal = TokenRegex().Replace(template, "");
        if (literal.Contains('{') || literal.Contains('}'))
        {
            errors.Add($"{label} contains an unclosed or malformed {{token}}.");
        }

        var illegal = literal
            .Where(character => IllegalLiteralCharacters.Contains(character) || char.IsControl(character))
            .Distinct()
            .ToArray();
        if (illegal.Length > 0)
        {
            errors.Add($"{label} contains characters that are illegal in file names: {string.Join(" ", illegal)}");
        }

        return errors;
    }

    public static string Pad(long value, int zeros) =>
        value.ToString(zeros > 0 ? $"D{zeros}" : "D", CultureInfo.InvariantCulture);

    // Chapter numbers can be fractional (e.g. "Chapter 10.5"); only the whole part is padded.
    public static string PadDecimal(double value, int zeros)
    {
        var whole = Math.Floor(value);
        var wholeText = Pad((long)whole, zeros);
        var fraction = value - whole;
        return fraction <= 0.0001
            ? wholeText
            : wholeText + fraction.ToString("0.#", CultureInfo.InvariantCulture)[1..];
    }

    private static string RenderToken(
        Match match,
        TokenResolver resolveToken,
        List<string>? errors)
    {
        var rawToken = match.Groups["token"].Value;
        var separator = match.Groups["separator"].Value;
        var pad = match.Groups["pad"].Value;
        var key = (separator.Length == 0 ? rawToken : rawToken.Replace(separator, " ", StringComparison.Ordinal))
            .ToLowerInvariant();

        var value = resolveToken(key, pad.Length, errors);
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        if (separator.Length > 0 && separator != " ")
        {
            value = value.Replace(" ", separator, StringComparison.Ordinal);
        }

        if (rawToken.Any(char.IsLetter))
        {
            if (rawToken.Where(char.IsLetter).All(char.IsLower))
            {
                value = value.ToLowerInvariant();
            }
            else if (rawToken.Where(char.IsLetter).All(char.IsUpper))
            {
                value = value.ToUpperInvariant();
            }
        }

        return CleanFileName(value);
    }
}
