using System.Text.RegularExpressions;

namespace AniLingo.Web.Features.Acquisition.Quality;

public static class AnimeReleaseScorer
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public static AnimeReleaseScoreResult Score(
        AnimeQualityProfile profile,
        AnimeReleaseCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(candidate.Release);

        var validation = ValidateProfile(profile);
        if (validation.Count > 0)
        {
            throw new ArgumentException(
                $"Quality profile '{profile.Id}' is invalid: {string.Join("; ", validation)}",
                nameof(profile));
        }

        var qualityKey = AnimeReleaseQuality.GetKey(candidate.Release);
        var qualityRank = IndexOf(profile.QualityOrder, qualityKey);
        var rejections = new List<string>();
        var scoreReasons = new List<string>();

        if (profile.AllowedQualities.Length > 0 &&
            !profile.AllowedQualities.Contains(qualityKey, StringComparer.OrdinalIgnoreCase))
        {
            rejections.Add($"Quality '{qualityKey}' is not allowed.");
        }

        if (profile.MinimumSizeBytes is long minimumSize &&
            candidate.SizeBytes is long actualSize &&
            actualSize < minimumSize)
        {
            rejections.Add($"Size {actualSize} B is below minimum {minimumSize} B.");
        }

        if (profile.MaximumSizeBytes is long maximumSize &&
            candidate.SizeBytes is long actualMaximumSize &&
            actualMaximumSize > maximumSize)
        {
            rejections.Add($"Size {actualMaximumSize} B is above maximum {maximumSize} B.");
        }

        foreach (var required in profile.MustContain)
        {
            if (!candidate.Release.RawTitle.Contains(required, StringComparison.OrdinalIgnoreCase))
            {
                rejections.Add($"Missing required term '{required}'.");
            }
        }

        foreach (var rejected in profile.MustNotContain)
        {
            if (candidate.Release.RawTitle.Contains(rejected, StringComparison.OrdinalIgnoreCase))
            {
                rejections.Add($"Contains rejected term '{rejected}'.");
            }
        }

        foreach (var pattern in profile.RequiredRegex)
        {
            if (!Regex.IsMatch(
                    candidate.Release.RawTitle,
                    pattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    RegexTimeout))
            {
                rejections.Add($"Does not match required regex '{pattern}'.");
            }
        }

        foreach (var pattern in profile.RejectedRegex)
        {
            if (Regex.IsMatch(
                    candidate.Release.RawTitle,
                    pattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    RegexTimeout))
            {
                rejections.Add($"Matches rejected regex '{pattern}'.");
            }
        }

        var score = 0;
        foreach (var rule in profile.ScoreRules)
        {
            if (!Matches(rule, candidate.Release))
            {
                continue;
            }

            score += rule.Score;
            scoreReasons.Add($"{rule.Name}: {(rule.Score >= 0 ? "+" : "")}{rule.Score}");
        }

        if (score < profile.MinimumScore)
        {
            rejections.Add($"Score {score} is below minimum {profile.MinimumScore}.");
        }

        return new AnimeReleaseScoreResult(
            candidate,
            Accepted: rejections.Count == 0,
            score,
            qualityKey,
            qualityRank,
            rejections,
            scoreReasons);
    }

    public static IReadOnlyList<AnimeReleaseScoreResult> Rank(
        AnimeQualityProfile profile,
        IEnumerable<AnimeReleaseCandidate> candidates) =>
        candidates
            .Select(candidate => Score(profile, candidate))
            .OrderByDescending(result => result.Accepted)
            .ThenBy(result => result.QualityRank)
            .ThenByDescending(result => result.Score)
            .ThenBy(result => result.Candidate.Release.RawTitle, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static bool IsUpgrade(
        AnimeQualityProfile profile,
        AnimeReleaseScoreResult current,
        AnimeReleaseScoreResult candidate)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(candidate);

        if (!profile.UpgradeAllowed || !candidate.Accepted)
        {
            return false;
        }

        var cutoffRank = string.IsNullOrWhiteSpace(profile.UpgradeCutoffQuality)
            ? int.MinValue
            : IndexOf(profile.QualityOrder, profile.UpgradeCutoffQuality);

        if (cutoffRank != int.MaxValue &&
            current.QualityRank <= cutoffRank)
        {
            return false;
        }

        if (candidate.QualityRank < current.QualityRank)
        {
            return true;
        }

        return candidate.QualityRank == current.QualityRank &&
               candidate.Score > current.Score;
    }

    public static IReadOnlyList<string> ValidateProfile(AnimeQualityProfile profile)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            errors.Add("Profile ID is required.");
        }

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            errors.Add("Profile name is required.");
        }

        if (profile.AllowedQualities is null ||
            profile.QualityOrder is null ||
            profile.MustContain is null ||
            profile.MustNotContain is null ||
            profile.RequiredRegex is null ||
            profile.RejectedRegex is null ||
            profile.ScoreRules is null)
        {
            errors.Add("Profile collections must not be null.");
            return errors;
        }

        if (profile.MinimumSizeBytes is < 0)
        {
            errors.Add("Minimum size must be zero or greater.");
        }

        if (profile.MaximumSizeBytes is < 0)
        {
            errors.Add("Maximum size must be zero or greater.");
        }

        if (profile.MinimumSizeBytes is long minimum &&
            profile.MaximumSizeBytes is long maximum &&
            minimum > maximum)
        {
            errors.Add("Minimum size must not exceed maximum size.");
        }

        if (profile.QualityOrder
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            errors.Add("Quality order contains duplicates.");
        }

        foreach (var allowed in profile.AllowedQualities)
        {
            if (!profile.QualityOrder.Contains(allowed, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"Allowed quality '{allowed}' is missing from quality order.");
            }
        }

        if (!string.IsNullOrWhiteSpace(profile.UpgradeCutoffQuality) &&
            !profile.QualityOrder.Contains(profile.UpgradeCutoffQuality, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"Upgrade cutoff '{profile.UpgradeCutoffQuality}' is missing from quality order.");
        }

        foreach (var pattern in profile.RequiredRegex.Concat(profile.RejectedRegex))
        {
            ValidateRegex(pattern, errors);
        }

        foreach (var rule in profile.ScoreRules)
        {
            if (string.IsNullOrWhiteSpace(rule.Name))
            {
                errors.Add("Score rule name is required.");
            }

            if (string.IsNullOrWhiteSpace(rule.Value))
            {
                errors.Add($"Score rule '{rule.Name}' value is required.");
            }

            if (rule.Match == AnimeReleaseRuleMatch.Regex)
            {
                ValidateRegex(rule.Value, errors);
            }
        }

        return errors;
    }

    private static bool Matches(
        AnimeReleaseScoreRule rule,
        AnimeReleaseInfo release)
    {
        var values = GetValues(rule.Field, release);
        return values.Any(value => rule.Match switch
        {
            AnimeReleaseRuleMatch.Equals =>
                value.Equals(rule.Value, StringComparison.OrdinalIgnoreCase),
            AnimeReleaseRuleMatch.Contains =>
                value.Contains(rule.Value, StringComparison.OrdinalIgnoreCase),
            AnimeReleaseRuleMatch.Regex =>
                Regex.IsMatch(
                    value,
                    rule.Value,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    RegexTimeout),
            _ => false
        });
    }

    private static IEnumerable<string> GetValues(
        AnimeReleaseRuleField field,
        AnimeReleaseInfo release) =>
        field switch
        {
            AnimeReleaseRuleField.RawTitle => [release.RawTitle],
            AnimeReleaseRuleField.ReleaseGroup =>
                Value(release.ReleaseGroup),
            AnimeReleaseRuleField.Source => [release.Source.ToString()],
            AnimeReleaseRuleField.Resolution =>
                release.Resolution is int resolution ? [resolution.ToString()] : [],
            AnimeReleaseRuleField.VideoCodec => [release.VideoCodec.ToString()],
            AnimeReleaseRuleField.BitDepth =>
                release.BitDepth is int bitDepth ? [bitDepth.ToString()] : [],
            AnimeReleaseRuleField.HdrFormat => [release.HdrFormat.ToString()],
            AnimeReleaseRuleField.AudioCodec => [release.AudioCodec.ToString()],
            AnimeReleaseRuleField.AudioLanguage => release.AudioLanguages,
            AnimeReleaseRuleField.SubtitleLanguage => release.SubtitleLanguages,
            AnimeReleaseRuleField.DualAudio => [release.IsDualAudio.ToString()],
            AnimeReleaseRuleField.MultiAudio => [release.IsMultiAudio.ToString()],
            AnimeReleaseRuleField.Proper => [release.IsProper.ToString()],
            AnimeReleaseRuleField.Repack => [release.IsRepack.ToString()],
            _ => []
        };

    private static IEnumerable<string> Value(string? value) =>
        string.IsNullOrWhiteSpace(value) ? [] : [value];

    private static int IndexOf(IReadOnlyList<string> values, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return int.MaxValue;
        }

        for (var index = 0; index < values.Count; index++)
        {
            if (values[index].Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    private static void ValidateRegex(string pattern, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            errors.Add("Regex pattern must not be empty.");
            return;
        }

        try
        {
            _ = new Regex(
                pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                RegexTimeout);
        }
        catch (ArgumentException)
        {
            errors.Add($"Invalid regex '{pattern}'.");
        }
    }
}
