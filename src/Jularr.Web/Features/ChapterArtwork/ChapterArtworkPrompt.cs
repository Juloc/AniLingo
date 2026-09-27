using System.Text;
using Jularr.Web.Features.StoryContext;

namespace Jularr.Web.Features.ChapterArtwork;

public sealed record ChapterArtworkPromptInput(
    string WorkTitle,
    IReadOnlyList<string> Genres,
    int ChapterNumber,
    string? ChapterTitle,
    ChapterArtworkStyle Style,
    string? SeriesStyle,
    StoryContextSnapshot Snapshot);

public sealed record ChapterArtworkPrompt(
    string Text,
    bool Neutral,
    string ContextHash);

/// <summary>
/// Builds the image prompt for chapter N from the spoiler-safe "known before
/// chapter N" snapshot only. It never sees chapter text, summaries or anything
/// first introduced in chapter N or later. Scenes stay atmospheric: a known
/// setting and, when their look is established, figures without faces or
/// names — no plot events. Chapter 1 (or a work without earlier knowledge)
/// gets a neutral image from book metadata and style alone.
/// </summary>
public static class ChapterArtworkPromptComposer
{
    public const int PromptVersion = 1;
    public const string ContextScope = "before-chapter";
    private const int DetailLength = 180;

    /// <summary>The minimal projection image generation needs from the shared story context.</summary>
    public static StoryContextQuery ContextQuery(int chapterNumber) =>
        StoryContextQuery.Before(chapterNumber) with
        {
            Fields = StoryContextFields.Entities
                | StoryContextFields.EntityDetails
                | StoryContextFields.Appearance
                | StoryContextFields.Themes,
            MaxEntities = 12,
            MaxThemes = 4,
            RecentChapterCount = 0,
            MaxEarlierChapters = 0
        };

    public static ChapterArtworkPrompt Compose(
        ChapterArtworkPromptInput input,
        bool forceNeutral = false)
    {
        var snapshot = input.Snapshot;
        if (snapshot.Scope != StoryContextScope.BeforeChapter
            || snapshot.Chapter != input.ChapterNumber)
        {
            throw new InvalidOperationException(
                "Chapter artwork needs the story context known before the target chapter.");
        }

        var useStory = !forceNeutral
            && input.ChapterNumber > 1
            && snapshot.HasStoryKnowledge;

        var setting = useStory
            ? snapshot.Entities
                .Where(x => x.Kind == StoryEntityKind.Location)
                .Select(x => Short(x.Appearance ?? x.Description))
                .FirstOrDefault(x => x is not null)
            : null;

        var figures = useStory
            ? snapshot.Entities
                .Where(x => x.Kind == StoryEntityKind.Character)
                .Select(x => Short(x.Appearance))
                .OfType<string>()
                .Take(2)
                .ToArray()
            : [];

        var neutral = setting is null && figures.Length == 0;
        var lines = new List<string>
        {
            $"Chapter header illustration for the book \"{input.WorkTitle.Trim()}\".",
            StyleText(input.Style)
        };

        if (!string.IsNullOrWhiteSpace(input.SeriesStyle))
        {
            lines.Add("Series art direction: " + input.SeriesStyle.Trim());
        }

        if (input.Genres.Count > 0)
        {
            lines.Add("Genre and mood: " + string.Join(", ", input.Genres.Take(6)) + ".");
        }

        if (useStory && snapshot.Themes.Count > 0)
        {
            lines.Add("Themes: " + string.Join(", ", snapshot.Themes) + ".");
        }

        if (neutral)
        {
            lines.Add(
                "Show an atmospheric scene without identifiable characters: a landscape, an interior, weather, a season or a symbolic motif that fits the genre. Do not depict story events.");
        }
        else
        {
            if (setting is not null)
            {
                lines.Add("Setting: " + setting);
            }

            if (figures.Length > 0)
            {
                lines.Add(
                    "Figures shown small, from a distance or from behind, faces not in focus: "
                    + string.Join("; ", figures));
            }

            lines.Add("A quiet moment rather than a dramatic event; do not depict fights, deaths, reveals or outcomes.");
        }

        if (!string.IsNullOrWhiteSpace(input.ChapterTitle))
        {
            lines.Add($"Chapter title for mood only: \"{input.ChapterTitle.Trim()}\".");
        }

        lines.Add(
            "Wide landscape composition with calm, low-detail space in the lower third for a title overlay, readable in light and dark themes. No text, letters, captions, logos or watermarks.");

        return new ChapterArtworkPrompt(
            StoryContextBuilder.FitLines(lines, StoryContextBudgets.ImagePrompt),
            neutral,
            snapshot.Hash);
    }

    public static string StyleText(ChapterArtworkStyle style) => style switch
    {
        ChapterArtworkStyle.Watercolor => "Style: soft watercolor with visible paper texture and gentle washes.",
        ChapterArtworkStyle.Ink => "Style: ink and wash illustration with fine linework and restrained colour.",
        ChapterArtworkStyle.Anime => "Style: light-novel anime illustration, clean lines, cinematic lighting.",
        ChapterArtworkStyle.Minimal => "Style: minimal flat illustration with a limited palette and simple shapes.",
        _ => "Style: painterly book illustration with rich light and atmospheric depth."
    };

    private static string? Short(string? value)
    {
        var clean = value?.Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            return null;
        }

        return clean.Length <= DetailLength
            ? clean
            : clean[..(DetailLength - 1)].TrimEnd() + "…";
    }
}

public sealed record ChapterArtworkSpoilerCheck(
    bool Passed,
    int NameHits,
    int TextOverlaps);

/// <summary>
/// Second, independent spoiler check on the finished prompt: it must not
/// mention anything the reader has not met before the chapter (names, aliases
/// and terms first seen in the chapter or later, or never placed) and must not
/// reuse passages of the target or later chapters.
/// </summary>
public static class ChapterArtworkSpoilerGuard
{
    private const int ShingleWords = 5;
    private const int ShingleCharacters = 8;

    /// <summary>
    /// Names that must not appear in the prompt of <paramref name="chapterNumber"/>.
    /// Names the reader already knows and words from safe metadata are allowed.
    /// </summary>
    public static IReadOnlyList<string> ForbiddenNames(
        StoryContextDocument? full,
        StoryContextSnapshot safe,
        int chapterNumber,
        IEnumerable<string> hiddenChapterTitles,
        IEnumerable<string> safeMetadata)
    {
        var forbidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in full?.Entities ?? [])
        {
            if (entity.FirstSeenChapter is not int seen || seen >= chapterNumber)
            {
                forbidden.Add(entity.Name);
            }

            // Aliases have no chapter and can reveal identities.
            foreach (var alias in entity.Aliases)
            {
                forbidden.Add(alias);
            }
        }

        foreach (var term in full?.Terms ?? [])
        {
            if (term.FirstSeenChapter is not int seen || seen >= chapterNumber)
            {
                forbidden.Add(term.Source);
            }
        }

        foreach (var title in hiddenChapterTitles)
        {
            forbidden.Add(title);
        }

        var metadata = string.Join("\n", safeMetadata);
        forbidden.RemoveWhere(name =>
            name.Trim().Length < 3
            || safe.Entities.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            || safe.Terms.Any(x => x.Source.Equals(name, StringComparison.OrdinalIgnoreCase))
            || ContainsWord(metadata, name));

        return forbidden.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    public static ChapterArtworkSpoilerCheck Check(
        string prompt,
        IEnumerable<string> forbiddenNames,
        IEnumerable<string> protectedTexts)
    {
        var nameHits = forbiddenNames.Count(name => ContainsWord(prompt, name));

        var promptShingles = Shingles(prompt);
        var overlaps = 0;
        foreach (var text in protectedTexts)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            overlaps += Shingles(text).Count(promptShingles.Contains);
        }

        return new ChapterArtworkSpoilerCheck(nameHits == 0 && overlaps == 0, nameHits, overlaps);
    }

    /// <summary>Case-insensitive whole-word match; scripts without spaces match anywhere.</summary>
    public static bool ContainsWord(string text, string name)
    {
        var needle = name.Trim();
        if (needle.Length == 0 || string.IsNullOrEmpty(text))
        {
            return false;
        }

        var spaceless = needle.Any(IsSpacelessScript);
        var index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var end = index + needle.Length;
            if (spaceless
                || ((index == 0 || !char.IsLetterOrDigit(text[index - 1]))
                    && (end >= text.Length || !char.IsLetterOrDigit(text[end]))))
            {
                return true;
            }

            index = end;
        }

        return false;
    }

    private static HashSet<string> Shingles(string text)
    {
        var shingles = new HashSet<string>(StringComparer.Ordinal);
        var normalized = Normalize(text);

        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index + ShingleWords <= words.Length; index++)
        {
            shingles.Add(string.Join(' ', words, index, ShingleWords));
        }

        // Scripts without spaces: compare character windows of the letters only.
        var letters = new string(normalized.Where(IsSpacelessScript).ToArray());
        for (var index = 0; index + ShingleCharacters <= letters.Length; index++)
        {
            shingles.Add(letters.Substring(index, ShingleCharacters));
        }

        return shingles;
    }

    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }

        return builder.ToString();
    }

    private static bool IsSpacelessScript(char value) =>
        value is >= '⺀' and <= '鿿'
            or >= '가' and <= '힯'
            or >= '豈' and <= '﫿'
            or >= '぀' and <= 'ヿ';
}
