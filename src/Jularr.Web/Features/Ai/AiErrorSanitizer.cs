using System.Text.RegularExpressions;

namespace Jularr.Web.Features.Ai;

/// <summary>
/// Makes provider errors safe to show and keep: removes credentials, query strings and control
/// characters, and caps the length. Raw prompts never reach this path.
/// </summary>
public static partial class AiErrorSanitizer
{
    private const int MaxLength = 300;

    public static string? Sanitize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var clean = BearerRegex().Replace(message, "Bearer [redacted]");
        clean = SecretRegex().Replace(clean, "[redacted]");
        clean = KeyValueSecretRegex().Replace(clean, "$1=[redacted]");
        clean = QueryRegex().Replace(clean, "$1");
        clean = ControlRegex().Replace(clean, " ").Trim();

        return clean.Length > MaxLength
            ? clean[..MaxLength].TrimEnd() + "…"
            : clean;
    }

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9._~+/=-]+", RegexOptions.IgnoreCase)]
    private static partial Regex BearerRegex();

    [GeneratedRegex(@"\b(?:sk|pk|rk|sess|org)-[A-Za-z0-9_-]{8,}\b|\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9._-]+")]
    private static partial Regex SecretRegex();

    [GeneratedRegex(@"\b(api[_-]?key|access[_-]?token|refresh[_-]?token|token|password|secret)\s*[=:]\s*""?[^\s"",;]+", RegexOptions.IgnoreCase)]
    private static partial Regex KeyValueSecretRegex();

    [GeneratedRegex(@"(https?://[^\s?#""']+)\?[^\s""']*", RegexOptions.IgnoreCase)]
    private static partial Regex QueryRegex();

    [GeneratedRegex(@"[\x00-\x1F\x7F]+")]
    private static partial Regex ControlRegex();
}
