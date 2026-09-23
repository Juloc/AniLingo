namespace AniLingo.Web.Features.Auth;

public sealed class OwnerAccount
{
    public const string SingletonId = "owner";

    public string Id { get; set; } = SingletonId;
    public string UserName { get; set; } = string.Empty;
    public string NormalizedUserName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
