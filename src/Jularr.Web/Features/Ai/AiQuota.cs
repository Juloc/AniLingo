namespace Jularr.Web.Features.Ai;

public sealed record AiQuotaWindow(
    double UsedPercent,
    int? WindowDurationMinutes,
    DateTimeOffset? ResetsAt)
{
    public double RemainingPercent => Math.Clamp(100 - UsedPercent, 0, 100);
}

public sealed record AiQuotaCredits(bool HasCredits, bool Unlimited, string? Balance);

public sealed record AiQuotaSpendLimit(
    string Limit,
    string Used,
    double RemainingPercent,
    DateTimeOffset? ResetsAt);

/// <summary>
/// One provider-reported limit bucket, shown as returned. <see cref="ModelSlug"/> is only set when
/// the protocol itself maps the bucket to a model; Jularr never infers that mapping.
/// </summary>
public sealed record AiQuotaBucket(
    string? LimitId,
    string? LimitName,
    string? ModelSlug,
    AiQuotaWindow? Primary,
    AiQuotaWindow? Secondary,
    AiQuotaCredits? Credits,
    AiQuotaSpendLimit? IndividualLimit,
    bool? SpendControlReached,
    string? PlanType,
    string? ReachedType);

/// <summary>Structured provider quota; absent values stay null and are shown as unavailable.</summary>
public sealed record AiQuotaSnapshot(
    bool? OrdinaryUsageAllowed,
    IReadOnlyList<AiQuotaBucket> Buckets,
    DateTimeOffset RefreshedAt,
    DateTimeOffset? UpdatedAt)
{
    public string? PlanType => Buckets.Select(x => x.PlanType).FirstOrDefault(x => x is not null);

    /// <summary>
    /// Merges a sparse rolling update: windows and credits present in the update replace the stored
    /// ones for the same bucket, while missing account metadata never clears a known value.
    /// </summary>
    public AiQuotaSnapshot Merge(AiQuotaBucket update, DateTimeOffset now)
    {
        var index = Buckets
            .Select((bucket, position) => (bucket, position))
            .Where(x => update.LimitId is null
                ? x.position == 0
                : string.Equals(x.bucket.LimitId, update.LimitId, StringComparison.Ordinal))
            .Select(x => (int?)x.position)
            .FirstOrDefault();

        var buckets = Buckets.ToList();
        if (index is null)
        {
            buckets.Add(update);
        }
        else
        {
            var current = buckets[index.Value];
            buckets[index.Value] = current with
            {
                LimitName = update.LimitName ?? current.LimitName,
                ModelSlug = update.ModelSlug ?? current.ModelSlug,
                Primary = update.Primary ?? current.Primary,
                Secondary = update.Secondary ?? current.Secondary,
                Credits = update.Credits ?? current.Credits,
                IndividualLimit = update.IndividualLimit ?? current.IndividualLimit,
                SpendControlReached = update.SpendControlReached ?? current.SpendControlReached,
                PlanType = update.PlanType ?? current.PlanType,
                ReachedType = update.ReachedType ?? current.ReachedType
            };
        }

        return this with { Buckets = buckets, UpdatedAt = now };
    }
}

public sealed record AiAccountInfo(
    string? Type,
    string? PlanType,
    bool RequiresAuthentication);

/// <summary>Server Codex diagnostics shown to the owner: transport, capabilities, account and quota.</summary>
public sealed record AiServerDiagnostics(
    AiProviderCapabilities Capabilities,
    string? ServerAgent,
    AiAccountInfo? Account,
    AiQuotaSnapshot? Quota,
    string? Error);
