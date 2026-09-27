using System.Text.RegularExpressions;

namespace Jularr.Tests;

// _Layout.cshtml shows TempData status messages as a toast on every page. A page that renders
// the same TempData key again shows the message twice.
[TestClass]
public sealed partial class LayoutStatusToastTests
{
    [TestMethod]
    public void PagesDoNotRenderTempDataKeysTheLayoutToastAlreadyShows()
    {
        var pagesRoot = Path.Combine(FindRepositoryRoot(), "src", "Jularr.Web", "Pages");
        var layoutPath = Path.Combine(pagesRoot, "Shared", "_Layout.cshtml");
        var toastKeys = TempDataIndexer()
            .Matches(File.ReadAllText(layoutPath))
            .Select(match => match.Groups["key"].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.IsTrue(toastKeys.Count > 0, "The layout toast no longer reads a TempData key; update this test.");

        var violations = new List<string>();
        foreach (var path in Directory.EnumerateFiles(pagesRoot, "*.cshtml*", SearchOption.AllDirectories))
        {
            if (string.Equals(path, layoutPath, StringComparison.OrdinalIgnoreCase)
                || !(path.EndsWith(".cshtml", StringComparison.Ordinal)
                     || path.EndsWith(".cshtml.cs", StringComparison.Ordinal)))
            {
                continue;
            }

            var text = File.ReadAllText(path);
            var view = path.EndsWith(".cshtml", StringComparison.Ordinal) ? text : ViewFor(path);
            if (view is not null && LayoutDisabled().IsMatch(view))
            {
                continue;
            }

            foreach (var key in toastKeys)
            {
                if (ReadsKey(text, key, isView: path.EndsWith(".cshtml", StringComparison.Ordinal)))
                {
                    violations.Add($"{Path.GetRelativePath(pagesRoot, path)} reads TempData[\"{key}\"]");
                }
            }
        }

        Assert.AreEqual(
            0,
            violations.Count,
            "The layout toast already shows these messages; remove the page-level copy:\n"
            + string.Join("\n", violations));
    }

    private static bool ReadsKey(string text, string key, bool isView)
    {
        var quoted = Regex.Escape($"\"{key}\"");
        if (isView)
        {
            // Views never write TempData, so any mention is a render.
            return Regex.IsMatch(text, $@"TempData\s*(\[\s*{quoted}\s*\]|\.Peek\(\s*{quoted}|\.TryGetValue\(\s*{quoted})");
        }

        // Page models may set the key for the toast; only reads count.
        return Regex.IsMatch(text, $@"TempData\s*\[\s*{quoted}\s*\](?!\s*=(?!=))")
            || Regex.IsMatch(text, $@"TempData\s*\.(Peek|TryGetValue)\(\s*{quoted}")
            || Regex.IsMatch(text, $@"\[TempData\]\s*public\s+[\w?<>]+\s+{Regex.Escape(key)}\b")
            || Regex.IsMatch(text, $@"\[TempData\(\s*Key\s*=\s*{quoted}\s*\)\]");
    }

    private static string? ViewFor(string pageModelPath)
    {
        var view = pageModelPath[..^".cs".Length];
        return File.Exists(view) ? File.ReadAllText(view) : null;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Jularr.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Jularr repository root.");
    }

    [GeneratedRegex(@"TempData\s*\[\s*""(?<key>[^""]+)""\s*\]")]
    private static partial Regex TempDataIndexer();

    [GeneratedRegex(@"Layout\s*=\s*null\s*;")]
    private static partial Regex LayoutDisabled();
}
