using System.Security.Claims;
using AniLingo.Web.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Auth;

public sealed class OwnerAuthService(
    AppDbContext db,
    IPasswordHasher<OwnerAccount> passwordHasher)
{
    public Task<bool> HasOwnerAsync(CancellationToken cancellationToken = default) =>
        db.OwnerAccounts
            .AsNoTracking()
            .AnyAsync(
                x => x.Id == OwnerAccount.SingletonId
                    && x.Role == AccountRole.Owner,
                cancellationToken);

    public async Task<OwnerAccount> CreateOwnerAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        var cleanedUserName = CleanUserName(userName);
        ValidatePassword(password);

        if (await db.OwnerAccounts.AnyAsync(cancellationToken))
        {
            throw new InvalidOperationException("The AniLingo owner account has already been created.");
        }

        var owner = new OwnerAccount
        {
            Id = OwnerAccount.SingletonId,
            UserName = cleanedUserName,
            NormalizedUserName = NormalizeUserName(cleanedUserName),
            Role = AccountRole.Owner,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        };
        owner.PasswordHash = passwordHasher.HashPassword(owner, password);

        db.OwnerAccounts.Add(owner);
        await db.SaveChangesAsync(cancellationToken);
        return owner;
    }

    public Task<OwnerAccount> CreateUserAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default) =>
        CreateUserCoreAsync(userName, password, isEnabled: true, cancellationToken);

    public Task<OwnerAccount> CreateRegistrationRequestAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default) =>
        CreateUserCoreAsync(userName, password, isEnabled: false, cancellationToken);

    private async Task<OwnerAccount> CreateUserCoreAsync(
        string userName,
        string password,
        bool isEnabled,
        CancellationToken cancellationToken)
    {
        var cleanedUserName = CleanUserName(userName);
        ValidatePassword(password);
        var normalized = NormalizeUserName(cleanedUserName);

        if (await db.OwnerAccounts.AnyAsync(
                x => x.NormalizedUserName == normalized,
                cancellationToken))
        {
            throw new InvalidOperationException("This user name already exists.");
        }

        var account = new OwnerAccount
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = cleanedUserName,
            NormalizedUserName = normalized,
            Role = AccountRole.User,
            IsEnabled = isEnabled,
            CreatedAt = DateTime.UtcNow
        };
        account.PasswordHash = passwordHasher.HashPassword(account, password);

        db.OwnerAccounts.Add(account);
        await db.SaveChangesAsync(cancellationToken);
        return account;
    }

    public async Task<IReadOnlyList<LocalAccountSummary>> ListAsync(
        CancellationToken cancellationToken = default) =>
        await db.OwnerAccounts
            .AsNoTracking()
            .OrderBy(x => x.Role)
            .ThenBy(x => x.UserName)
            .Select(x => new LocalAccountSummary(
                x.Id,
                x.UserName,
                x.Role,
                x.IsEnabled,
                x.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task SetEnabledAsync(
        string accountId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var account = await db.OwnerAccounts
            .SingleOrDefaultAsync(x => x.Id == accountId, cancellationToken)
            ?? throw new InvalidOperationException("Account was not found.");

        if (account.Role == AccountRole.Owner && !enabled)
        {
            throw new InvalidOperationException("The owner account cannot be disabled.");
        }

        account.IsEnabled = enabled;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ResetPasswordAsync(
        string accountId,
        string password,
        CancellationToken cancellationToken = default)
    {
        ValidatePassword(password);

        var account = await db.OwnerAccounts
            .SingleOrDefaultAsync(x => x.Id == accountId, cancellationToken)
            ?? throw new InvalidOperationException("Account was not found.");

        account.PasswordHash = passwordHasher.HashPassword(account, password);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<OwnerAccount?> ValidateCredentialsAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeUserName(userName);
        var account = await db.OwnerAccounts
            .SingleOrDefaultAsync(
                x => x.NormalizedUserName == normalized,
                cancellationToken);

        if (account is null || !account.IsEnabled)
        {
            return null;
        }

        var result = passwordHasher.VerifyHashedPassword(
            account,
            account.PasswordHash,
            password);
        if (result == PasswordVerificationResult.Failed)
        {
            return null;
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            account.PasswordHash = passwordHasher.HashPassword(account, password);
            await db.SaveChangesAsync(cancellationToken);
        }

        return account;
    }

    public async Task<OwnerAccount?> GetEnabledAccountAsync(
        string accountId,
        CancellationToken cancellationToken = default) =>
        await db.OwnerAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == accountId && x.IsEnabled,
                cancellationToken);

    public static ClaimsPrincipal CreatePrincipal(OwnerAccount account)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, account.Id),
            new Claim(ClaimTypes.Name, account.UserName),
            new Claim(ClaimTypes.Role, account.Role.ToString())
        ],
        CookieAuthenticationDefaults.AuthenticationScheme);

        return new ClaimsPrincipal(identity);
    }

    public static string? GetAccountId(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier);

    private static string CleanUserName(string userName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        var cleaned = userName.Trim();

        if (cleaned.Length > 80)
        {
            throw new ArgumentException(
                "User name must be 80 characters or fewer.",
                nameof(userName));
        }

        return cleaned;
    }

    private static string NormalizeUserName(string userName) =>
        (userName ?? string.Empty).Trim().ToUpperInvariant();

    private static void ValidatePassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        if (password.Length < 12)
        {
            throw new ArgumentException(
                "Password must contain at least 12 characters.",
                nameof(password));
        }
    }
}
