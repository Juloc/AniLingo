using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Tests;

[TestClass]
public sealed class AccountGroupStoreTests
{
    [TestMethod]
    public async Task GroupsPersistMembershipsAndCascadeWithAccounts()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"jularr-account-groups-{Guid.NewGuid():N}.db");

        try
        {
            await using var db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite($"Data Source={path};Foreign Keys=True")
                    .Options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var auth = new OwnerAuthService(
                db,
                new PasswordHasher<OwnerAccount>());
            var owner = await auth.CreateOwnerAsync(
                "owner",
                "a sufficiently long owner password");
            var user = await auth.CreateUserAsync(
                "reader",
                "a sufficiently long user password");

            var groups = new AccountGroupStore(db);
            var family = await groups.CreateAsync("Family");

            Assert.AreEqual(0, (await groups.ListAsync()).Single().MemberCount);

            await groups.SetMembershipsAsync(user.Id, [family.Id]);
            CollectionAssert.AreEquivalent(
                new[] { family.Id },
                (await groups.GetMembershipIdsAsync(user.Id)).ToArray());
            Assert.AreEqual(1, (await groups.ListAsync()).Single().MemberCount);

            await groups.RenameAsync(family.Id, "Household");
            Assert.AreEqual("Household", (await groups.ListAsync()).Single().Name);

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => groups.CreateAsync("household"));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => groups.SetMembershipsAsync(owner.Id, [family.Id]));

            await auth.DeleteUserAsync(user.Id);
            Assert.AreEqual(0, (await groups.ListAsync()).Single().MemberCount);

            await groups.DeleteAsync(family.Id);
            Assert.AreEqual(0, (await groups.ListAsync()).Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void UsersAndUserDetailExposeGroupManagementWithoutASecondRoleModel()
    {
        var root = RepositoryRoot();
        var users = File.ReadAllText(
            Path.Combine(root, "src", "Jularr.Web", "Pages", "Admin", "Users.cshtml"));
        var user = File.ReadAllText(
            Path.Combine(root, "src", "Jularr.Web", "Pages", "Admin", "User.cshtml"));
        var groups = File.ReadAllText(
            Path.Combine(root, "src", "Jularr.Web", "Pages", "Admin", "Groups.cshtml"));

        StringAssert.Contains(users, "asp-page=\"/Admin/Groups\"");
        StringAssert.Contains(user, "asp-page-handler=\"SetGroups\"");
        StringAssert.Contains(user, "name=\"groupIds\"");
        StringAssert.Contains(groups, "data-admin-groups");
        StringAssert.Contains(groups, "AccountRole.Owner");
        StringAssert.Contains(groups, "AccountRole.MediaManager");
        StringAssert.Contains(groups, "AccountRole.User");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Jularr.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Jularr repository root.");
    }
}
