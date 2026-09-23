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
            .AnyAsync(x => x.Id == OwnerAccount.SingletonId, cancellationToken);

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
            CreatedAt = DateTime.UtcNow
        };
        owner.PasswordHash = passwordHasher.HashPassword(owner, password);

        db.OwnerAccounts.Add(owner);
        await db.SaveChangesAsync(cancellationToken);
        return owner;
    }

    public async Task<OwnerAccount?> ValidateCredentialsAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        var owner = await db.OwnerAccounts
            .SingleOrDefaultAsync(x => x.Id == OwnerAccount.SingletonId, cancellationToken);

        if (owner is null
            || !string.Equals(
                owner.NormalizedUserName,
                NormalizeUserName(userName),
                StringComparison.Ordinal))
        {
            return null;
        }

        var result = passwordHasher.VerifyHashedPassword(owner, owner.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
        {
            return null;
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            owner.PasswordHash = passwordHasher.HashPassword(owner, password);
            await db.SaveChangesAsync(cancellationToken);
        }

        return owner;
    }

    public static ClaimsPrincipal CreatePrincipal(OwnerAccount owner)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, owner.Id),
            new Claim(ClaimTypes.Name, owner.UserName)
        ],
        CookieAuthenticationDefaults.AuthenticationScheme);

        return new ClaimsPrincipal(identity);
    }

    private static string CleanUserName(string userName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        var cleaned = userName.Trim();

        if (cleaned.Length > 80)
        {
            throw new ArgumentException("User name must be 80 characters or fewer.", nameof(userName));
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
            throw new ArgumentException("Password must contain at least 12 characters.", nameof(password));
        }
    }
}
