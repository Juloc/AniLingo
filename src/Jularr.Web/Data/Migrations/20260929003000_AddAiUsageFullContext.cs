using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations;

/// <summary>
/// AI usage (#422, #412): the estimated size of the shared story context before it was compacted
/// for each request, next to the existing "ContextTokens" (the compact projection actually sent),
/// so usage shows how much context the projection saved. Counter only; the table is not part of
/// the EF model.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260929003000_AddAiUsageFullContext")]
public sealed class AddAiUsageFullContext : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "AiUsageDaily" ADD COLUMN "FullContextTokens" INTEGER NOT NULL DEFAULT 0;
            UPDATE "AiUsageDaily" SET "FullContextTokens" = "ContextTokens";
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "AiUsageDaily" DROP COLUMN "FullContextTokens";
            """);
    }
}
