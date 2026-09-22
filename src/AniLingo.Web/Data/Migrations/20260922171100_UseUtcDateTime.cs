using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260922171100_UseUtcDateTime")]
public sealed class UseUtcDateTime : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // DateTimeOffset and DateTime both use SQLite TEXT storage.
        // Existing alpha timestamps were written in UTC, so no physical rewrite is required.
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
