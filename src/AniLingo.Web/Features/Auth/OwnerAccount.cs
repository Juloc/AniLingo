using System.ComponentModel.DataAnnotations.Schema;

namespace AniLingo.Web.Features.Auth;

public enum AccountRole
{
    Owner = 1,
    User = 2
}

public static class AccountRoles
{
    public const string Owner = nameof(AccountRole.Owner);
    public const string User = nameof(AccountRole.User);
}

public sealed class OwnerAccount
{
    public const string SingletonId = "owner";

    public string Id { get; set; } = SingletonId;
    public string UserName { get; set; } = string.Empty;
    public string NormalizedUserName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public AccountRole Role { get; set; } = AccountRole.Owner;
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [NotMapped]
    public long SessionVersion { get; set; } = 1;
}

public sealed record LocalAccountSummary(
    string Id,
    string UserName,
    AccountRole Role,
    bool IsEnabled,
    DateTime CreatedAt);
