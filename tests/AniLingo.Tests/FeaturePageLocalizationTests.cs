using System.Text.RegularExpressions;

namespace AniLingo.Tests;

/// <summary>
/// Guards the #185 localization migration of Books, Manga, Discover, Admin,
/// Reading, Kana, Settings (except DownloadClients/Indexers, migrated
/// separately), Acquisition, Appearance, Artwork, Companion,
/// LocalizationAdmin, LocalizationPreferences and Statistics pages: their
/// headings and buttons must come from the UI catalog instead of being
/// hard-coded English literals. This intentionally scans only
/// headings/buttons (not every text node) because these feature pages
/// legitimately render dynamic user/library content (titles, file names,
/// provider identifiers) in many other elements.
/// </summary>
[TestClass]
public sealed partial class FeaturePageLocalizationTests
{
    // Proper nouns, product names and technical/format identifiers that are
    // allowed to appear untranslated verbatim inside a heading or button.
    private static readonly string[] AllowedLiteralText =
    [
        "AniLingo",
        "AniList",
        "EPUB",
        "CBZ",
        "ZIP",
        "OPDS",
        "Sonarr",
        "SABnzbd",
        "ISBN",
        "AI",
        "API",
        "URL",
        "OpenAI",
        "Codex CLI",
        "M"
    ];

    private static readonly string[] MigratedFolders =
    [
        "Books", "Manga", "Discover", "Admin", "Reading", "Kana",
        "Settings", "Acquisition", "Appearance", "Artwork", "Companion",
        "LocalizationAdmin", "LocalizationPreferences", "Statistics"
    ];

    // Sub-folders of a migrated folder that are still owned by other
    // in-flight work and are intentionally excluded from this scan.
    private static readonly string[] ExcludedRelativeDirectories =
    [
        Path.Combine("Settings", "DownloadClients"),
        Path.Combine("Settings", "Indexers")
    ];

    [TestMethod]
    public void MigratedFeaturePagesHaveNoHardCodedHeadingsOrButtons()
    {
        var pagesRoot = Path.Combine(RepositoryRoot(), "src", "AniLingo.Web", "Pages");
        var files = MigratedFolders
            .SelectMany(folder => Directory.EnumerateFiles(
                Path.Combine(pagesRoot, folder),
                "*.cshtml",
                SearchOption.AllDirectories))
            .Where(file => !ExcludedRelativeDirectories.Any(excluded =>
                file.Contains(
                    Path.DirectorySeparatorChar + excluded + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal)))
            .ToArray();

        Assert.IsTrue(files.Length >= 40, "Expected page files from the migrated feature areas.");

        var findings = files
            .SelectMany(file => FindHeadingOrButtonLiteralText(File.ReadAllText(file))
                .Select(text => $"{Path.GetFileName(file)}: \"{text}\""))
            .ToArray();

        Assert.AreEqual(0, findings.Length, string.Join(Environment.NewLine, findings));
    }

    private static IEnumerable<string> FindHeadingOrButtonLiteralText(string razor)
    {
        var markup = StripCodeBlocks(Regex.Replace(razor, @"@\*.*?\*@", string.Empty, RegexOptions.Singleline));
        markup = Regex.Replace(markup, @"<script\b[^>]*>.*?</script>", "<script></script>", RegexOptions.Singleline);

        foreach (Match tag in HeadingOrButtonElement().Matches(markup))
        {
            var body = tag.Groups["body"].Value;
            var text = Regex.Replace(body, @"<[^>]+>", " ").Trim();
            text = Regex.Replace(text, @"\s+", " ");

            if (text.Length > 0
                && !text.Contains('@')
                && text.Any(char.IsLetter)
                && !AllowedLiteralText.Contains(text))
            {
                yield return text;
            }
        }
    }

    private static string StripCodeBlocks(string razor)
    {
        var result = new System.Text.StringBuilder(razor.Length);
        for (var index = 0; index < razor.Length; index++)
        {
            if (razor[index] == '@' && index + 1 < razor.Length && razor[index + 1] == '{')
            {
                var depth = 0;
                for (index++; index < razor.Length; index++)
                {
                    depth += razor[index] switch { '{' => 1, '}' => -1, _ => 0 };
                    if (depth == 0)
                    {
                        break;
                    }
                }

                continue;
            }

            result.Append(razor[index]);
        }

        return result.ToString();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AniLingo.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AniLingo repository root.");
    }

    [GeneratedRegex(@"<(?<tag>h1|h2|button)\b[^>]*>(?<body>.*?)</\k<tag>>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex HeadingOrButtonElement();
}
