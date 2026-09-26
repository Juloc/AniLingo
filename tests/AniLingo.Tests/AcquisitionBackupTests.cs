using AniLingo.Web.Features.Acquisition.Backup;
using AniLingo.Web.Features.Acquisition.Import;
using AniLingo.Web.Features.Acquisition.Policy;

namespace AniLingo.Tests;

/// <summary>P1 item 8: backup/restore of acquisition settings, with a dry-run preview and secret handling.</summary>
[TestClass]
public sealed class AcquisitionBackupTests
{
    [TestMethod]
    public async Task ExportThenRestoreRoundTripsCanonicalStores()
    {
        await using var environment = await AnimeAcquisitionEnvironment.CreateAsync();
        await environment.SeedFrierenAsync();
        await environment.ImportSettings.UpdateAsync(state => state with { DefaultImportMode = AnimeImportMode.Copy });
        await environment.Policy.UpdateAsync(state => state with { Tags = [new AcquisitionTag("t1", "Tag 1")] });

        var bundle = await environment.ExportBackupAsync();
        Assert.IsTrue(bundle.Files.ContainsKey("monitoring.json"));
        Assert.IsTrue(bundle.Files.ContainsKey("import-settings.json"));
        Assert.IsTrue(bundle.Files.ContainsKey("acquisition-policy.json"));
        Assert.IsTrue(bundle.Files.ContainsKey("prowlarr.json"));
        Assert.IsTrue(bundle.Files.ContainsKey("sabnzbd.json"));

        // Change settings after the export.
        await environment.ImportSettings.UpdateAsync(state => state with { DefaultImportMode = AnimeImportMode.Move });
        await environment.Policy.UpdateAsync(state => state with { Tags = [] });

        var restored = await environment.RestoreBackupAsync(bundle);

        Assert.IsTrue(restored.Success, string.Join(" ", restored.Errors));
        Assert.AreEqual(bundle.Files.Count, restored.FilesWritten);
        Assert.AreEqual(AnimeImportMode.Copy, (await environment.ImportSettings.LoadAsync()).DefaultImportMode);
        Assert.AreEqual("Tag 1", (await environment.Policy.LoadAsync()).Tags.Single().Name);
    }

    [TestMethod]
    public async Task PreviewNeverWritesAnything()
    {
        await using var environment = await AnimeAcquisitionEnvironment.CreateAsync();
        await environment.SeedFrierenAsync();
        await environment.ImportSettings.UpdateAsync(state => state with { DefaultImportMode = AnimeImportMode.Copy });
        var bundle = await environment.ExportBackupAsync();
        await environment.ImportSettings.UpdateAsync(state => state with { DefaultImportMode = AnimeImportMode.Move });

        var preview = await environment.PreviewRestoreAsync(bundle);

        Assert.IsTrue(preview.CanRestore);
        var importFile = preview.Files.Single(file => file.FileName == "import-settings.json");
        Assert.IsTrue(importFile.WouldChange, "The preview reports what would change...");
        Assert.AreEqual(AnimeImportMode.Move, (await environment.ImportSettings.LoadAsync()).DefaultImportMode, "...without applying it.");
    }

    [TestMethod]
    public async Task AnUnsupportedBundleVersionIsRejectedWithoutWritingAnything()
    {
        await using var environment = await AnimeAcquisitionEnvironment.CreateAsync();
        await environment.SeedFrierenAsync();
        await environment.ImportSettings.UpdateAsync(state => state with { DefaultImportMode = AnimeImportMode.Copy });

        var badBundle = new AcquisitionBackupBundle(99, DateTimeOffset.UtcNow, new Dictionary<string, string>
        {
            ["import-settings.json"] = """{"Version":1,"DefaultImportMode":"Move","RootImportModes":{},"RemotePathMappings":[]}"""
        });

        var result = await environment.RestoreBackupAsync(badBundle);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, result.FilesWritten);
        Assert.AreEqual(AnimeImportMode.Copy, (await environment.ImportSettings.LoadAsync()).DefaultImportMode, "Nothing was written.");
    }

    [TestMethod]
    public async Task InvalidJsonInTheBundleIsRejectedWithoutWritingAnything()
    {
        await using var environment = await AnimeAcquisitionEnvironment.CreateAsync();
        await environment.SeedFrierenAsync();
        await environment.ImportSettings.UpdateAsync(state => state with { DefaultImportMode = AnimeImportMode.Copy });

        var badBundle = new AcquisitionBackupBundle(AcquisitionBackupService.CurrentVersion, DateTimeOffset.UtcNow, new Dictionary<string, string>
        {
            ["import-settings.json"] = "not json"
        });

        var preview = await environment.PreviewRestoreAsync(badBundle);
        Assert.IsFalse(preview.CanRestore);

        var result = await environment.RestoreBackupAsync(badBundle);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(AnimeImportMode.Copy, (await environment.ImportSettings.LoadAsync()).DefaultImportMode);
    }

    [TestMethod]
    public async Task ProwlarrAndSabnzbdBackupsAreFlaggedAsCarryingEncryptedSecrets()
    {
        await using var environment = await AnimeAcquisitionEnvironment.CreateAsync();
        await environment.SeedFrierenAsync();

        var bundle = await environment.ExportBackupAsync();
        var preview = await environment.PreviewRestoreAsync(bundle);

        Assert.IsTrue(preview.Files.Single(f => f.FileName == "prowlarr.json").ContainsEncryptedSecret);
        Assert.IsTrue(preview.Files.Single(f => f.FileName == "sabnzbd.json").ContainsEncryptedSecret);
        Assert.IsFalse(preview.Files.Single(f => f.FileName == "monitoring.json").ContainsEncryptedSecret);
        // The secret itself is never in plain text in the bundle.
        StringAssert.Contains(bundle.Files["prowlarr.json"], "protectedApiKey");
        StringAssert.DoesNotMatch(bundle.Files["prowlarr.json"], new System.Text.RegularExpressions.Regex("prowlarr-key"));
    }
}
