namespace Jularr.Web.Features.Ai;

public enum AiBudgetState
{
    /// <summary>No daily limit is configured.</summary>
    Unlimited,
    Ok,
    Warning,
    Reached
}

/// <summary>
/// Today's usage against the profile's Jularr-local daily token limit. Tokens are input plus output
/// as recorded in the durable usage aggregates, estimates included. Provider quota is separate.
/// </summary>
public sealed record AiBudgetStatus(long UsedTokens, int? LimitTokens, int WarningPercent)
{
    public double? Percent =>
        LimitTokens is > 0 ? Math.Min(100, UsedTokens * 100d / LimitTokens.Value) : null;

    public AiBudgetState State =>
        LimitTokens is not > 0
            ? AiBudgetState.Unlimited
            : UsedTokens >= LimitTokens
                ? AiBudgetState.Reached
                : UsedTokens * 100 >= (long)LimitTokens.Value * WarningPercent
                    ? AiBudgetState.Warning
                    : AiBudgetState.Ok;

    public static AiBudgetStatus For(AiProfileSettings settings, AiUsageTotals today) =>
        new(today.AllInputTokens + today.AllOutputTokens, settings.DailyTokenBudget, settings.EffectiveWarningPercent);
}

/// <summary>Stops AI work before a request is sent once the profile's daily limit is used up.</summary>
public sealed class AiBudgetExceededException(string message) : InvalidOperationException(message)
{
    public const string DefaultMessage =
        "The daily AI token limit of this profile is used up. AI tasks continue tomorrow (UTC) or after the limit is raised in Settings → AI.";
}

/// <summary>Checks the daily limit against today's durable usage before an AI request starts.</summary>
public sealed class AiBudgetGuard(AiUsageStore usage, TimeProvider time)
{
    public async Task<AiBudgetStatus> GetStatusAsync(
        string profileId,
        AiProfileSettings settings,
        CancellationToken cancellationToken)
    {
        if (settings.DailyTokenBudget is not > 0)
        {
            return new AiBudgetStatus(0, null, settings.EffectiveWarningPercent);
        }

        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var report = await usage.GetReportAsync(profileId, today, AiUsagePeriod.Today, cancellationToken);
        return AiBudgetStatus.For(settings, report.Today);
    }

    public async Task EnsureAvailableAsync(
        string profileId,
        AiProfileSettings settings,
        CancellationToken cancellationToken)
    {
        if ((await GetStatusAsync(profileId, settings, cancellationToken)).State == AiBudgetState.Reached)
        {
            throw new AiBudgetExceededException(AiBudgetExceededException.DefaultMessage);
        }
    }
}

/// <summary>
/// This process run's usage against the profile's Jularr-local session token limit. Unlike
/// <see cref="AiBudgetStatus"/>, session usage is in-memory only and resets when Jularr restarts.
/// </summary>
public sealed record AiSessionBudgetStatus(long UsedTokens, int? LimitTokens)
{
    public double? Percent =>
        LimitTokens is > 0 ? Math.Min(100, UsedTokens * 100d / LimitTokens.Value) : null;

    public bool IsReached => LimitTokens is > 0 && UsedTokens >= LimitTokens;

    public static AiSessionBudgetStatus For(AiProfileSettings settings, AiUsageSnapshot usage) =>
        new(usage.InputTokens + usage.OutputTokens, settings.SessionTokenBudget);
}

/// <summary>Stops AI work before a request is sent once the profile's session limit is used up.</summary>
public sealed class AiSessionBudgetExceededException(string message) : InvalidOperationException(message)
{
    public const string DefaultMessage =
        "The session AI token limit of this profile is used up. AI tasks continue after Jularr restarts or after the limit is raised in Settings → AI.";
}

/// <summary>Checks the session limit against this run's in-memory usage before an AI request starts.</summary>
public sealed class AiSessionBudgetGuard(AiUsageTracker usage)
{
    public AiSessionBudgetStatus GetStatus(string profileId, AiProfileSettings settings) =>
        AiSessionBudgetStatus.For(settings, usage.GetSnapshot(profileId));

    public void EnsureAvailable(string profileId, AiProfileSettings settings)
    {
        if (GetStatus(profileId, settings).IsReached)
        {
            throw new AiSessionBudgetExceededException(AiSessionBudgetExceededException.DefaultMessage);
        }
    }
}
