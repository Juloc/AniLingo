using System.Text.Json;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Acquisition.AniListAutoMonitor;
using AniLingo.Web.Features.Acquisition.Backup;
using AniLingo.Web.Features.Acquisition.Import;
using AniLingo.Web.Features.Acquisition.Policy;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Pages.Settings;

/// <summary>
/// The one settings page for the P1 import/policy backlog: import mode (global and per library
/// root), remote path mappings, tags, delay profiles, tag-scoped indexer restrictions, the current
/// profile's AniList list auto-monitor rule, and acquisition settings backup/restore.
/// </summary>
[Authorize(Roles = AccountRoles.Owner)]
public sealed class AcquisitionModel(
    AnimeImportSettingsStore importSettings,
    AcquisitionPolicyStore policyStore,
    AniListAutoMonitorSettingsStore aniListAutoMonitorStore,
    AcquisitionBackupService backupService,
    CurrentAccountContext currentAccount,
    AppDbContext db) : PageModel
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public AnimeImportSettingsState ImportSettings { get; private set; } = AnimeImportSettingsState.Empty();
    public AcquisitionPolicyState Policy { get; private set; } = AcquisitionPolicyState.Empty();
    public IReadOnlyList<LibraryRoot> Roots { get; private set; } = [];
    public bool AniListAutoMonitorEnabled { get; private set; }
    public AcquisitionBackupPreview? RestorePreview { get; private set; }
    public string? PendingRestoreJson { get; private set; }
    public string? Notice => TempData["AcquisitionSettingsNotice"] as string;
    public string? Error => TempData["AcquisitionSettingsError"] as string;

    public async Task OnGetAsync(CancellationToken cancellationToken) => await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostImportModeAsync(
        AnimeImportMode defaultImportMode,
        Guid? rootId,
        AnimeImportMode? rootImportMode,
        CancellationToken cancellationToken)
    {
        await importSettings.UpdateAsync(
            state =>
            {
                var roots = new Dictionary<Guid, AnimeImportMode>(state.RootImportModes);
                if (rootId is { } id)
                {
                    if (rootImportMode is { } mode)
                    {
                        roots[id] = mode;
                    }
                    else
                    {
                        roots.Remove(id);
                    }
                }

                return state with { DefaultImportMode = defaultImportMode, RootImportModes = roots };
            },
            cancellationToken);
        TempData["AcquisitionSettingsNotice"] = "Import mode saved.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddPathMappingAsync(
        string remotePrefix,
        string localPrefix,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(remotePrefix) || string.IsNullOrWhiteSpace(localPrefix))
        {
            TempData["AcquisitionSettingsError"] = "Both the remote and local prefix are required.";
            return RedirectToPage();
        }

        await importSettings.UpdateAsync(
            state =>
            {
                var mappings = state.RemotePathMappings
                    .Where(mapping => !mapping.RemotePrefix.Equals(remotePrefix.Trim(), StringComparison.OrdinalIgnoreCase))
                    .Append(new RemotePathMapping(remotePrefix.Trim(), localPrefix.Trim()))
                    .ToList();
                return state with { RemotePathMappings = mappings };
            },
            cancellationToken);
        TempData["AcquisitionSettingsNotice"] = "Path mapping saved.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemovePathMappingAsync(string remotePrefix, CancellationToken cancellationToken)
    {
        await importSettings.UpdateAsync(
            state => state with
            {
                RemotePathMappings = state.RemotePathMappings
                    .Where(mapping => !mapping.RemotePrefix.Equals(remotePrefix, StringComparison.OrdinalIgnoreCase))
                    .ToList()
            },
            cancellationToken);
        TempData["AcquisitionSettingsNotice"] = "Path mapping removed.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddTagAsync(string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["AcquisitionSettingsError"] = "Enter a tag name.";
            return RedirectToPage();
        }

        await policyStore.UpdateAsync(
            state =>
            {
                var id = name.Trim().ToLowerInvariant().Replace(' ', '-');
                if (state.Tags.Any(tag => tag.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                {
                    return state;
                }

                var tags = state.Tags.Append(new AcquisitionTag(id, name.Trim())).ToList();
                return state with { Tags = tags };
            },
            cancellationToken);
        TempData["AcquisitionSettingsNotice"] = "Tag added.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveTagAsync(string tagId, CancellationToken cancellationToken)
    {
        await policyStore.UpdateAsync(
            state => state with
            {
                Tags = state.Tags.Where(tag => !tag.Id.Equals(tagId, StringComparison.OrdinalIgnoreCase)).ToList(),
                DelayProfiles = state.DelayProfiles
                    .Select(profile => profile with { TagIds = profile.TagIds.Where(id => !id.Equals(tagId, StringComparison.OrdinalIgnoreCase)).ToArray() })
                    .ToList(),
                IndexerRestrictions = state.IndexerRestrictions
                    .Select(restriction => restriction with { TagIds = restriction.TagIds.Where(id => !id.Equals(tagId, StringComparison.OrdinalIgnoreCase)).ToArray() })
                    .ToList()
            },
            cancellationToken);
        TempData["AcquisitionSettingsNotice"] = "Tag removed.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddDelayProfileAsync(
        string name,
        int delayMinutes,
        string? qualityProfileId,
        string[]? tagIds,
        bool isDefault,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name) || delayMinutes < 0)
        {
            TempData["AcquisitionSettingsError"] = "Enter a name and a non-negative delay.";
            return RedirectToPage();
        }

        await policyStore.UpdateAsync(
            state =>
            {
                var profiles = state.DelayProfiles.Append(new AnimeDelayProfile(
                    Guid.NewGuid().ToString("N"),
                    name.Trim(),
                    delayMinutes,
                    string.IsNullOrWhiteSpace(qualityProfileId) ? null : qualityProfileId.Trim(),
                    tagIds ?? [],
                    isDefault)).ToList();
                return state with { DelayProfiles = profiles };
            },
            cancellationToken);
        TempData["AcquisitionSettingsNotice"] = "Delay profile added.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveDelayProfileAsync(string profileId, CancellationToken cancellationToken)
    {
        await policyStore.UpdateAsync(
            state => state with { DelayProfiles = state.DelayProfiles.Where(profile => profile.Id != profileId).ToList() },
            cancellationToken);
        TempData["AcquisitionSettingsNotice"] = "Delay profile removed.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddIndexerRestrictionAsync(
        string name,
        string[]? tagIds,
        string allowedIndexerIds,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name) || tagIds is not { Length: > 0 })
        {
            TempData["AcquisitionSettingsError"] = "Enter a name and select at least one tag.";
            return RedirectToPage();
        }

        var ids = (allowedIndexerIds ?? "")
            .Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, out var id) ? id : (int?)null)
            .Where(id => id is > 0)
            .Select(id => id!.Value)
            .Distinct()
            .Order()
            .ToArray();

        await policyStore.UpdateAsync(
            state =>
            {
                var restrictions = state.IndexerRestrictions.Append(
                    new AnimeIndexerRestriction(Guid.NewGuid().ToString("N"), name.Trim(), tagIds, ids)).ToList();
                return state with { IndexerRestrictions = restrictions };
            },
            cancellationToken);
        TempData["AcquisitionSettingsNotice"] = "Indexer restriction added.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveIndexerRestrictionAsync(string restrictionId, CancellationToken cancellationToken)
    {
        await policyStore.UpdateAsync(
            state => state with { IndexerRestrictions = state.IndexerRestrictions.Where(item => item.Id != restrictionId).ToList() },
            cancellationToken);
        TempData["AcquisitionSettingsNotice"] = "Indexer restriction removed.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAniListAutoMonitorAsync(bool enabled, CancellationToken cancellationToken)
    {
        await aniListAutoMonitorStore.SetEnabledAsync(currentAccount.ProfileId, enabled, DateTimeOffset.UtcNow, cancellationToken);
        TempData["AcquisitionSettingsNotice"] = enabled
            ? "AniList Current/Planning auto-monitor is on for this profile."
            : "AniList Current/Planning auto-monitor is off for this profile.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostExportBackupAsync(CancellationToken cancellationToken)
    {
        var bundle = await backupService.ExportAsync(cancellationToken);
        var json = JsonSerializer.Serialize(bundle, JsonOptions);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        return File(bytes, "application/json", $"anilingo-acquisition-backup-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");
    }

    public async Task<IActionResult> OnPostPreviewRestoreAsync(IFormFile backupFile, CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        if (backupFile is null || backupFile.Length == 0)
        {
            TempData["AcquisitionSettingsError"] = "Choose a backup file.";
            return RedirectToPage();
        }

        using var reader = new StreamReader(backupFile.OpenReadStream());
        var json = await reader.ReadToEndAsync(cancellationToken);
        try
        {
            var bundle = JsonSerializer.Deserialize<AcquisitionBackupBundle>(json, JsonOptions);
            if (bundle is null)
            {
                TempData["AcquisitionSettingsError"] = "The backup file is empty or invalid.";
                return RedirectToPage();
            }

            RestorePreview = await backupService.PreviewRestoreAsync(bundle, cancellationToken);
            PendingRestoreJson = json;
        }
        catch (JsonException)
        {
            TempData["AcquisitionSettingsError"] = "The backup file is not valid JSON.";
            return RedirectToPage();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostApplyRestoreAsync(string pendingRestoreJson, CancellationToken cancellationToken)
    {
        try
        {
            var bundle = JsonSerializer.Deserialize<AcquisitionBackupBundle>(pendingRestoreJson, JsonOptions);
            if (bundle is null)
            {
                TempData["AcquisitionSettingsError"] = "The backup could not be read.";
                return RedirectToPage();
            }

            var result = await backupService.RestoreAsync(bundle, cancellationToken);
            TempData[result.Success ? "AcquisitionSettingsNotice" : "AcquisitionSettingsError"] = result.Success
                ? $"Restored {result.FilesWritten} acquisition settings file(s). Reload any open acquisition pages."
                : string.Join(" ", result.Errors);
        }
        catch (JsonException)
        {
            TempData["AcquisitionSettingsError"] = "The backup could not be read.";
        }

        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        ImportSettings = await importSettings.LoadAsync(cancellationToken);
        Policy = await policyStore.LoadAsync(cancellationToken);
        Roots = await db.LibraryRoots.AsNoTracking().OrderBy(root => root.Name).ToArrayAsync(cancellationToken);
        var autoMonitor = await aniListAutoMonitorStore.LoadAsync(cancellationToken);
        AniListAutoMonitorEnabled = autoMonitor.IsEnabled(currentAccount.ProfileId);
    }
}
