namespace AniLingo.Web.Features.Ai;

public sealed record AiProviderStatus(
    string Id,
    string DisplayName,
    bool IsAvailable,
    bool IsAuthenticated,
    string? Version,
    string? AuthenticationMethod,
    string? Error);

public interface IAiProvider
{
    string Id { get; }
    string DisplayName { get; }

    Task<AiProviderStatus> GetStatusAsync(CancellationToken cancellationToken);
}

public enum DeviceLoginState
{
    Idle,
    Starting,
    WaitingForUser,
    Succeeded,
    Failed,
    Cancelled
}

public sealed record DeviceLoginSnapshot(
    DeviceLoginState State,
    string? VerificationUrl,
    string? UserCode,
    string? Message,
    DateTimeOffset? StartedAt)
{
    public bool IsActive => State is DeviceLoginState.Starting or DeviceLoginState.WaitingForUser;
}
