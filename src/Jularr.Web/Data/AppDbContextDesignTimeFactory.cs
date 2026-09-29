using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Jularr.Web.Data;

/// <summary>
/// Lets the EF Core tooling (<c>dotnet ef migrations …</c>) build an <see cref="AppDbContext"/>
/// without starting the web host. No database connection is opened for
/// <c>migrations add</c> / <c>has-pending-model-changes</c>; the connection string is only a
/// placeholder so the Npgsql provider is configured.
/// </summary>
public sealed class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5432;Database=jularr;Username=jularr;Password=jularr";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
