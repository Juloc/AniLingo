using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AniLingo.Web.Features.MediaMapping;

public enum AutomaticMediaMatchDisposition
{
    None,
    Review,
    Auto
}

public sealed record AutomaticMediaMatchInput(
    string Title,
    IReadOnlyList<string>? AlternateTitles = null,
    int? Year = null,
    int? UnitCount = null,
    string? Format = null);

public sealed record AutomaticMediaMatchCandidate(
    string Provider,
    string ExternalId,
    string PreferredTitle,
    IReadOnlyList<string> Titles,
    int? Year = null,
    int? UnitCount = null,
    string? Format = null);

public sealed record AutomaticMediaMatchDecision(
    AutomaticMediaMatchDisposition Disposition,
    AutomaticMediaMatchCandidate? Candidate,
    int Score,
    int RunnerUpScore,
    IReadOnlyList<string> Evidence)
{
    public bool CanApply => Disposition == AutomaticMediaMatchDisposition.Auto;
}

public sealed record ExternalProgressResolution(
    bool CanSync,
    int Progress,
    string? Reason = null);

public sealed record MediaSegmentMapping(
    double LocalStart,
    double LocalEnd,
    int RemoteStart)
{
    public bool Contains(double localUnit) =>
        localUnit >= LocalStart && localUnit <= LocalEnd;

    public int Resolve(double localUnit)
    {
        if (!Contains(localUnit))
        {
            throw new ArgumentOutOfRangeException(nameof(localUnit));
        }

        var offset = localUnit - LocalStart;
        if (Math.Abs(offset - Math.Round(offset)) > 0.0001)
        {
            throw new InvalidOperationException(
                "Fractional local units cannot be translated to AniList integer progress.");
        }

        return checked(RemoteStart + (int)Math.Round(offset));
    }
}

public static partial class AutomaticMediaMatcher
{
    public const int AutoApplyScore = 85;
    public const int MinimumAutoLead = 10;
    public const int ReviewScore = 55;

    public static AutomaticMediaMatchDecision Select(
        AutomaticMediaMatchInput input,
        IEnumerable<AutomaticMediaMatchCandidate> candidates)
    {
        var ranked = candidates
            .Select(candidate => Score(input, candidate))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Candidate.ExternalId, StringComparer.Ordinal)
            .ToArray();

        if (ranked.Length == 0)
        {
            return new AutomaticMediaMatchDecision(
                AutomaticMediaMatchDisposition.None,
                null,
                0,
                0,
                ["No provider candidates were found."]);
        }

        var winner = ranked[0];
        var runnerUp = ranked.Length > 1 ? ranked[1].Score : 0;
        var lead = winner.Score - runnerUp;

        var disposition =
            winner.Score >= AutoApplyScore && lead >= MinimumAutoLead
                ? AutomaticMediaMatchDisposition.Auto
                : winner.Score >= ReviewScore
                    ? AutomaticMediaMatchDisposition.Review
                    : AutomaticMediaMatchDisposition.None;

        return new AutomaticMediaMatchDecision(
            disposition,
            winner.Candidate,
            winner.Score,
            runnerUp,
            winner.Evidence);
    }

    public static ExternalProgressResolution ResolveReadingProgress(
        double localUnitNumber,
        int position,
        int completedThreshold)
    {
        if (localUnitNumber <= 0 ||
            Math.Abs(localUnitNumber - Math.Round(localUnitNumber)) > 0.0001)
        {
            return new ExternalProgressResolution(
                false,
                0,
                "AniList progress is integer-based; this local chapter number needs an explicit mapping.");
        }

        var unit = checked((int)Math.Round(localUnitNumber));
        var progress = position >= completedThreshold
            ? unit
            : unit - 1;

        return progress > 0
            ? new ExternalProgressResolution(true, progress)
            : new ExternalProgressResolution(
                false,
                0,
                "Finish the first chapter before syncing AniList progress.");
    }

    public static ExternalProgressResolution ResolveMappedProgress(
        double localUnitNumber,
        int position,
        int completedThreshold,
        MediaSegmentMapping mapping)
    {
        var completedLocalUnit = position >= completedThreshold
            ? localUnitNumber
            : localUnitNumber - 1;

        if (completedLocalUnit < mapping.LocalStart)
        {
            return new ExternalProgressResolution(
                false,
                0,
                "No mapped chapter has been completed yet.");
        }

        if (!mapping.Contains(completedLocalUnit))
        {
            return new ExternalProgressResolution(
                false,
                0,
                "The current local chapter is outside the mapped AniList range.");
        }

        try
        {
            return new ExternalProgressResolution(
                true,
                mapping.Resolve(completedLocalUnit));
        }
        catch (InvalidOperationException exception)
        {
            return new ExternalProgressResolution(false, 0, exception.Message);
        }
    }

    private static ScoredCandidate Score(
        AutomaticMediaMatchInput input,
        AutomaticMediaMatchCandidate candidate)
    {
        var evidence = new List<string>();
        var score = 0;

        var inputTitles = BuildTitles(input.Title, input.AlternateTitles);
        var candidateTitles = BuildTitles(candidate.PreferredTitle, candidate.Titles);

        var exactPrimary = NormalizeTitle(input.Title) == NormalizeTitle(candidate.PreferredTitle);
        var exactAny = inputTitles.Overlaps(candidateTitles);

        if (exactPrimary)
        {
            score += 70;
            evidence.Add("exact preferred title");
        }
        else if (exactAny)
        {
            score += 65;
            evidence.Add("exact title/alias");
        }
        else
        {
            var similarity = inputTitles.Count == 0 || candidateTitles.Count == 0
                ? 0d
                : inputTitles.Max(left =>
                    candidateTitles.Max(right => TokenSimilarity(left, right)));

            var titleScore = (int)Math.Round(similarity * 55d);
            score += titleScore;
            if (titleScore > 0)
            {
                evidence.Add($"title similarity {similarity:P0}");
            }
        }

        if (input.Year is > 0 && candidate.Year is > 0)
        {
            var delta = Math.Abs(input.Year.Value - candidate.Year.Value);
            if (delta == 0)
            {
                score += 15;
                evidence.Add("same year");
            }
            else if (delta == 1)
            {
                score += 8;
                evidence.Add("year differs by one");
            }
            else
            {
                score -= 8;
                evidence.Add("different year");
            }
        }

        if (input.UnitCount is > 0 && candidate.UnitCount is > 0)
        {
            var delta = Math.Abs(input.UnitCount.Value - candidate.UnitCount.Value);
            if (delta == 0)
            {
                score += 15;
                evidence.Add("same episode/chapter count");
            }
            else if (delta == 1)
            {
                score += 7;
                evidence.Add("episode/chapter count differs by one");
            }
            else
            {
                score -= Math.Min(12, delta);
                evidence.Add("different episode/chapter count");
            }
        }

        if (!string.IsNullOrWhiteSpace(input.Format) &&
            !string.IsNullOrWhiteSpace(candidate.Format))
        {
            if (FormatsCompatible(input.Format, candidate.Format))
            {
                score += 10;
                evidence.Add("compatible format");
            }
            else
            {
                score -= 25;
                evidence.Add("incompatible format");
            }
        }

        return new ScoredCandidate(
            candidate,
            Math.Clamp(score, 0, 100),
            evidence);
    }

    private static HashSet<string> BuildTitles(
        string? primary,
        IReadOnlyList<string>? alternates)
    {
        var titles = new HashSet<string>(StringComparer.Ordinal);
        Add(primary);
        if (alternates is not null)
        {
            foreach (var title in alternates)
            {
                Add(title);
            }
        }

        return titles;

        void Add(string? value)
        {
            var normalized = NormalizeTitle(value);
            if (normalized.Length > 0)
            {
                titles.Add(normalized);
            }
        }
    }

    private static double TokenSimilarity(string left, string right)
    {
        if (left == right)
        {
            return 1d;
        }

        var leftTokens = left.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);
        var rightTokens = right.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);

        if (leftTokens.Count == 0 || rightTokens.Count == 0)
        {
            return 0d;
        }

        var intersection = leftTokens.Count(rightTokens.Contains);
        var union = leftTokens.Count + rightTokens.Count - intersection;
        return union == 0 ? 0d : (double)intersection / union;
    }

    private static bool FormatsCompatible(string left, string right)
    {
        var a = left.Trim().ToUpperInvariant();
        var b = right.Trim().ToUpperInvariant();

        if (a == b)
        {
            return true;
        }

        if (a is "LIGHT_NOVEL" or "NOVEL")
        {
            return b == "NOVEL";
        }

        if (a == "MANGA")
        {
            return b is "MANGA" or "ONE_SHOT";
        }

        if (a == "ANIME")
        {
            return b is "TV" or "TV_SHORT" or "ONA" or "OVA" or "MOVIE" or "SPECIAL" or "MUSIC";
        }

        return false;
    }

    private static string NormalizeTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }

        return WhitespaceRegex()
            .Replace(builder.ToString(), " ")
            .Trim();
    }

    private sealed record ScoredCandidate(
        AutomaticMediaMatchCandidate Candidate,
        int Score,
        IReadOnlyList<string> Evidence);

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
