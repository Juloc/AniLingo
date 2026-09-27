using System.Text.RegularExpressions;
using AniLingo.Web.Features.Localization;
using Microsoft.AspNetCore.Http;

namespace AniLingo.Tests;

[TestClass]
public sealed partial class AppShellNavigationTests
{
    private static readonly string[] AllowedLiteralText = ["Jularr"];

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void MobileNavigationIsBoundedAndStillReachesEveryDestination(
        bool learningVisible,
        bool isOwner)
    {
        var nav = UiShellNavigation.Build("/", learningVisible, isOwner);

        Assert.IsTrue(nav.MobilePrimary.Count <= UiShellNavigation.MaxMobilePrimaryItems);
        Assert.IsTrue(nav.MobilePrimary.Count >= 3);

        var desktop = nav.Primary.Concat(nav.Secondary).Select(x => x.Id).ToArray();
        var mobile = nav.MobilePrimary.Concat(nav.MobileMore).Select(x => x.Id).ToArray();
        CollectionAssert.AreEquivalent(desktop, mobile);
        Assert.AreEqual(mobile.Length, mobile.Distinct(StringComparer.Ordinal).Count());

        CollectionAssert.Contains(mobile, "books");
        CollectionAssert.Contains(mobile, "settings");
        Assert.AreEqual(isOwner, mobile.Contains("admin"));
        Assert.AreEqual(learningVisible, mobile.Contains("learn"));
    }

    [TestMethod]
    public void SettingsAndAdminStayOutOfPrimaryDestinations()
    {
        var nav = UiShellNavigation.Build("/", learningVisible: true, isOwner: true);

        CollectionAssert.AreEqual(
            new[] { "settings", "admin" },
            nav.Secondary.Select(x => x.Id).ToArray());
        Assert.IsFalse(nav.Primary.Any(x => x.Id is "settings" or "admin"));
        Assert.IsFalse(nav.MobilePrimary.Any(x => x.Id is "settings" or "admin"));
        CollectionAssert.AreEqual(
            new[] { "home", "library", "reading", "learn" },
            nav.MobilePrimary.Select(x => x.Id).ToArray());
    }

    [TestMethod]
    public void DiscoverTakesTheLearningSlotWhenLearningIsOff()
    {
        var nav = UiShellNavigation.Build("/", learningVisible: false, isOwner: false);

        CollectionAssert.AreEqual(
            new[] { "home", "library", "reading", "discover" },
            nav.MobilePrimary.Select(x => x.Id).ToArray());
    }

    [TestMethod]
    [DataRow("/", "home", false)]
    [DataRow("/Library/Anime/7a4c", "library", false)]
    [DataRow("/Novels/Read/7a4c", "reading", false)]
    [DataRow("/Manga", "reading", false)]
    [DataRow("/Books/Read/7a4c", "books", true)]
    [DataRow("/Discover", "discover", true)]
    [DataRow("/Learn/Kana", "learn", false)]
    [DataRow("/Statistics", "learn", false)]
    [DataRow("/Settings/Language", "settings", true)]
    [DataRow("/Admin/Languages", "admin", true)]
    public void CurrentLocationMarksExactlyOneDestination(
        string path,
        string expectedId,
        bool insideMoreMenu)
    {
        var nav = UiShellNavigation.Build(path, learningVisible: true, isOwner: true);

        var active = nav.Primary.Concat(nav.Secondary).Where(x => x.IsActive).ToArray();
        Assert.AreEqual(1, active.Length);
        Assert.AreEqual(expectedId, active[0].Id);
        Assert.AreEqual(insideMoreMenu, nav.MoreIsActive);
    }

    [TestMethod]
    public void NavigationLabelsAreCatalogKeysWithShortLengthGuidance()
    {
        var nav = UiShellNavigation.Build("/", learningVisible: true, isOwner: true);

        foreach (var item in nav.Primary.Concat(nav.Secondary))
        {
            Assert.IsTrue(
                UiTranslationResources.TryGet(item.LabelKey, out var message),
                $"Missing catalog key {item.LabelKey}.");
            Assert.AreEqual("Navigation", message.Surface, item.LabelKey);
            Assert.IsTrue(message.MaxLength is > 0 and <= 18, item.LabelKey);
        }

        Assert.IsTrue(UiTranslationResources.TryGet("nav.more", out var more));
        Assert.IsTrue(more.MaxLength <= 12, "The More tab shares a narrow bottom-bar cell.");
    }

    [TestMethod]
    public void ShellMarkupHasNoHardCodedUiText()
    {
        var root = RepositoryRoot();
        var pages = Path.Combine(root, "src", "AniLingo.Web", "Pages");
        var files = Directory.EnumerateFiles(Path.Combine(pages, "Shared"), "_App*.cshtml")
            .Where(x => !x.EndsWith("_AppIcon.cshtml", StringComparison.Ordinal))
            .Append(Path.Combine(pages, "Shared", "_Layout.cshtml"))
            .Concat(Directory.EnumerateFiles(Path.Combine(pages, "Account"), "*.cshtml"))
            .Append(Path.Combine(pages, "Settings", "Index.cshtml"))
            .ToArray();

        Assert.IsTrue(files.Length >= 10, "Expected the shell partials, layout, account and settings pages.");

        var findings = files
            .SelectMany(file => FindLiteralText(File.ReadAllText(file))
                .Select(text => $"{Path.GetFileName(file)}: \"{text}\""))
            .ToArray();

        Assert.AreEqual(0, findings.Length, string.Join(Environment.NewLine, findings));
    }

    [TestMethod]
    public void NavigationUsesRealIconsInsteadOfLetterGlyphs()
    {
        var shared = Path.Combine(RepositoryRoot(), "src", "AniLingo.Web", "Pages", "Shared");
        var navigation = File.ReadAllText(Path.Combine(shared, "_AppNavigation.cshtml"));
        var icons = File.ReadAllText(Path.Combine(shared, "_AppIcon.cshtml"));

        Assert.IsFalse(navigation.Contains("nav-glyph", StringComparison.Ordinal));
        Assert.IsFalse(navigation.Contains("brand-mark\">A<", StringComparison.Ordinal));
        StringAssert.Contains(navigation, "UiShellNavigation.Build");

        var nav = UiShellNavigation.Build("/", learningVisible: true, isOwner: true);
        foreach (var icon in nav.Primary.Concat(nav.Secondary).Select(x => x.Icon).Append("more"))
        {
            StringAssert.Contains(icons, $"case \"{icon}\":", $"Missing icon {icon}.");
        }
    }

    [TestMethod]
    public void ReadmeDoesNotHardCodeTheBuildVersion()
    {
        var root = RepositoryRoot();
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        var project = File.ReadAllText(Path.Combine(root, "src", "AniLingo.Web", "AniLingo.Web.csproj"));

        StringAssert.Matches(project, new Regex(@"<Version>\d+\.\d+\.\d+[^<]*</Version>"));
        Assert.IsFalse(
            Regex.IsMatch(readme, @"\b\d+\.\d+\.\d+-(alpha|beta|rc)\.\d+\b"),
            "README must point to the canonical version source instead of repeating a version.");
        StringAssert.Contains(readme, "src/AniLingo.Web/AniLingo.Web.csproj");
    }

    private static IEnumerable<string> FindLiteralText(string razor)
    {
        var markup = StripCodeBlocks(Regex.Replace(razor, @"@\*.*?\*@", string.Empty, RegexOptions.Singleline));
        markup = Regex.Replace(markup, @"<script\b[^>]*>.*?</script>", "<script></script>", RegexOptions.Singleline);

        foreach (Match match in Regex.Matches(markup, @">([^<>]+)<"))
        {
            var text = match.Groups[1].Value.Trim();
            if (text.Length > 0
                && !text.Contains('@')
                && text.Any(char.IsLetter)
                && !AllowedLiteralText.Contains(text))
            {
                yield return text;
            }
        }

        foreach (Match match in UserFacingAttribute().Matches(markup))
        {
            var value = match.Groups["value"].Value;
            if (value.Any(char.IsLetter) && !value.Contains('@'))
            {
                yield return $"{match.Groups["name"].Value}={value}";
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

    [GeneratedRegex(@"\b(?<name>aria-label|title|placeholder|alt)=""(?<value>[^""]*)""")]
    private static partial Regex UserFacingAttribute();
}
