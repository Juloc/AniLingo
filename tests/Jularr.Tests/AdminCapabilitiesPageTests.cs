namespace Jularr.Tests;

/// <summary>
/// Keeps Admin → Media capabilities aligned with instance-wide module availability.
/// </summary>
[TestClass]
public sealed class AdminCapabilitiesPageTests
{
    [TestMethod]
    public void EditorUsesOnlyEnabledCapabilityFamiliesAndPreservesHiddenRowsOnSave()
    {
        var root = RepositoryRoot();
        var model = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "Jularr.Web",
                "Pages",
                "Admin",
                "Capabilities",
                "Index.cshtml.cs"));
        var page = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "Jularr.Web",
                "Pages",
                "Admin",
                "Capabilities",
                "Index.cshtml"));

        StringAssert.Contains(model, "EnabledMediaTypesAsync");
        StringAssert.Contains(model, "InstanceModuleMedia.IsCapabilityFamilyEnabled");
        StringAssert.Contains(model, "foreach (var mediaType in mediaTypes)");
        Assert.AreEqual(
            2,
            System.Text.RegularExpressions.Regex.Matches(
                model,
                "foreach \\(var mediaType in mediaTypes\\)")
                .Count,
            "Role and per-user saves must both restrict writes to currently visible media rows.");

        StringAssert.Contains(page, "Model.MediaTypes.Count == 0");
        StringAssert.Contains(page, "admin.capabilities.noMediaModules");
        StringAssert.Contains(page, "capability-matrix-scroll");
        StringAssert.Contains(page, "id=\"user-@user.Id\"");
    }

    [TestMethod]
    public void SharedBookCapabilityIsNamedForBooksAndAudiobooks()
    {
        var translations = File.ReadAllText(
            Path.Combine(
                RepositoryRoot(),
                "src",
                "Jularr.Web",
                "Features",
                "Localization",
                "UiTranslationResources.cs"));

        StringAssert.Contains(
            translations,
            "admin.capabilities.mediaType.book\", \"Books & audiobooks");
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
}
