namespace Jularr.Tests;

[TestClass]
public sealed class AdminRolesOverviewTests
{
    [TestMethod]
    public void RolesOverviewUsesCanonicalRoleAndPolicyTables()
    {
        var root = RepositoryRoot();
        var page = File.ReadAllText(
            Path.Combine(root, "src", "Jularr.Web", "Pages", "Admin", "Roles.cshtml"));
        var model = File.ReadAllText(
            Path.Combine(root, "src", "Jularr.Web", "Pages", "Admin", "Roles.cshtml.cs"));

        StringAssert.Contains(page, "data-admin-roles");
        StringAssert.Contains(page, "AccountRoles.LabelKey(role.Role)");
        StringAssert.Contains(page, "JularrPolicies.LabelKey(policy)");
        StringAssert.Contains(page, "asp-page=\"/Admin/Capabilities/Index\"");
        StringAssert.Contains(page, "asp-page=\"/Admin/Users\"");

        StringAssert.Contains(model, "AccountRole.Owner");
        StringAssert.Contains(model, "AccountRole.MediaManager");
        StringAssert.Contains(model, "AccountRole.User");
        StringAssert.Contains(model, "JularrPolicies.Roles");
        StringAssert.Contains(model, "accounts.ListAsync(cancellationToken)");

        Assert.IsFalse(
            page.Contains("custom group", StringComparison.OrdinalIgnoreCase),
            "The page must not pretend an undefined custom-group policy model exists.");
    }

    [TestMethod]
    public void UsersNavigationOwnsRolesAndCapabilities()
    {
        var root = RepositoryRoot();
        var catalog = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "Jularr.Web",
                "Features",
                "Localization",
                "UiShellNavigation.cs"));

        StringAssert.Contains(
            catalog,
            "[\"/Admin/Users\", \"/Admin/User\", \"/Admin/Roles\", \"/Admin/Capabilities\"]");
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
