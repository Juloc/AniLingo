using System.Text.Json;

namespace Jularr.Web.Features.Acquisition.Access;

/// <summary>
/// The one canonical store of the owner's request settings (auto-approval rules and the quality profiles
/// requesters may pick). It is durable-but-not-relational configuration, so it uses the JSON settings-store
/// pattern under <c>/data</c> (like <c>MediaCapabilityStore</c>) rather than an EF table.
/// </summary>
public sealed class AcquisitionRequestSettingsStore
{
    public const string FileName = "request-settings.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string path;

    public AcquisitionRequestSettingsStore(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        path = Path.Combine(dataRoot, "acquisition", FileName);
    }

    public async Task<AcquisitionRequestSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadUnlockedAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Appends a rule (rules are evaluated in the order they were added).</summary>
    public Task<AcquisitionRequestSettings> AddRuleAsync(AutoApprovalRule rule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return MutateAsync(
            settings => settings with { AutoApprovalRules = [.. settings.AutoApprovalRules, rule] },
            cancellationToken);
    }

    public Task<AcquisitionRequestSettings> RemoveRuleAsync(string ruleId, CancellationToken cancellationToken = default) =>
        MutateAsync(
            settings => settings with
            {
                AutoApprovalRules = [.. settings.AutoApprovalRules.Where(rule => rule.Id != ruleId)]
            },
            cancellationToken);

    public Task<AcquisitionRequestSettings> SetRuleEnabledAsync(
        string ruleId,
        bool enabled,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            settings => settings with
            {
                AutoApprovalRules =
                [
                    .. settings.AutoApprovalRules.Select(rule => rule.Id == ruleId ? rule with { Enabled = enabled } : rule)
                ]
            },
            cancellationToken);

    /// <summary>Replaces the quality profiles requesters may pick (none = requesters cannot pick one).</summary>
    public Task<AcquisitionRequestSettings> SetRequesterQualityProfilesAsync(
        IEnumerable<string> profileIds,
        CancellationToken cancellationToken = default)
    {
        string[] ids = [.. profileIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(StringComparer.Ordinal)];
        return MutateAsync(settings => settings with { RequesterQualityProfileIds = ids }, cancellationToken);
    }

    private async Task<AcquisitionRequestSettings> MutateAsync(
        Func<AcquisitionRequestSettings, AcquisitionRequestSettings> mutate,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var updated = mutate(await LoadUnlockedAsync(cancellationToken));
            await SaveUnlockedAsync(updated, cancellationToken);
            return updated;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<AcquisitionRequestSettings> LoadUnlockedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return AcquisitionRequestSettings.Default;
        }

        PersistedSettings? persisted;
        try
        {
            persisted = JsonSerializer.Deserialize<PersistedSettings>(
                await File.ReadAllTextAsync(path, cancellationToken),
                JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Request settings '{path}' are invalid JSON.", exception);
        }

        if (persisted is null)
        {
            return AcquisitionRequestSettings.Default;
        }

        List<AutoApprovalRule> rules = [];
        foreach (var rule in persisted.AutoApprovalRules ?? [])
        {
            if (string.IsNullOrWhiteSpace(rule.Id) || string.IsNullOrWhiteSpace(rule.Name))
            {
                continue;
            }

            MediaAcquisitionKind[] kinds =
                [.. (rule.Kinds ?? []).Select(TryParseKind).OfType<MediaAcquisitionKind>().Distinct().Order()];
            if (kinds.Length == 0 && rule.Kinds is { Count: > 0 })
            {
                // Every media type of this rule is unknown to this build; an empty list would mean
                // "all media types", so the rule is left out instead of being widened.
                continue;
            }

            rules.Add(new AutoApprovalRule(
                rule.Id,
                rule.Name,
                rule.Enabled,
                kinds,
                [.. (rule.ProfileIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id))],
                rule.Quota is { MaxRequests: > 0, PeriodDays: > 0 } quota
                    ? new AutoApprovalQuota(quota.MaxRequests, quota.PeriodDays)
                    : null));
        }

        return new AcquisitionRequestSettings(
            rules,
            [.. (persisted.RequesterQualityProfileIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id))]);
    }

    private async Task SaveUnlockedAsync(AcquisitionRequestSettings settings, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var persisted = new PersistedSettings(
            [
                .. settings.AutoApprovalRules.Select(rule => new PersistedRule(
                    rule.Id,
                    rule.Name,
                    rule.Enabled,
                    [.. rule.Kinds.Select(AcquisitionAccessNames.Kind)],
                    [.. rule.ProfileIds],
                    rule.Quota is { } quota ? new PersistedQuota(quota.MaxRequests, quota.PeriodDays) : null))
            ],
            [.. settings.RequesterQualityProfileIds]);

        var temporary = $"{path}.tmp-{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(persisted, JsonOptions), cancellationToken);
            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static MediaAcquisitionKind? TryParseKind(string value)
    {
        try
        {
            return AcquisitionAccessNames.ParseKind(value);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private sealed record PersistedSettings(
        List<PersistedRule>? AutoApprovalRules,
        List<string>? RequesterQualityProfileIds);

    private sealed record PersistedRule(
        string Id,
        string Name,
        bool Enabled,
        List<string>? Kinds,
        List<string>? ProfileIds,
        PersistedQuota? Quota);

    private sealed record PersistedQuota(int MaxRequests, int PeriodDays);
}
