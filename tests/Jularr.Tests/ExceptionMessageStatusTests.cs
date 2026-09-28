using System.Text.RegularExpressions;

namespace Jularr.Tests;

/// <summary>
/// Page handlers must never surface a raw exception message in a TempData status value or a
/// status/error property (#523): the exception is logged instead, and the user sees a
/// localized message from UiTranslationResources. This guards every page handler source file
/// against that pattern regressing.
/// </summary>
[TestClass]
public sealed partial class ExceptionMessageStatusTests
{
    [TestMethod]
    public void PageHandlersNeverAssignExceptionMessageToStatus()
    {
        var pagesRoot = Path.Combine(RepositoryRoot(), "src", "Jularr.Web", "Pages");
        var files = Directory.EnumerateFiles(pagesRoot, "*.cshtml.cs", SearchOption.AllDirectories).ToArray();

        Assert.IsTrue(files.Length > 50, "Expected page handler source files under src/Jularr.Web/Pages.");

        var violations = new List<string>();
        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            foreach (Match match in RawExceptionMessageAssignment().Matches(source))
            {
                var line = source[..match.Index].Count(c => c == '\n') + 1;
                violations.Add($"{Path.GetFileName(file)}:{line}: {match.Value.Trim()}");
            }
        }

        Assert.AreEqual(
            0,
            violations.Count,
            "Page handlers must log the exception and show a localized status instead of the raw exception message:\n"
            + string.Join("\n", violations));
    }

    private static string RepositoryRoot()
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

    // Matches an assignment (`=`, `??=`) or ternary else-branch (`:`) whose value is a bare
    // exception/ex .Message member access ending the statement, e.g.
    // `TempData["Status"] = exception.Message;`, `Error = exception.Message;` or
    // `... ? Ui[...] : exception.Message;`. Deliberately does not match the message being
    // interpolated into a larger localized template (`ui.Format(key, ("message",
    // exception.Message))`) or written to the durable operation log
    // (`$"{exception.GetType().Name}: {exception.Message}"`), neither of which puts the raw
    // message on its own as the status value shown to the user.
    [GeneratedRegex(@"[=:]\s*(exception|ex)\.Message\s*;")]
    private static partial Regex RawExceptionMessageAssignment();
}
