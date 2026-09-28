using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations;

/// <summary>
/// Provider roles and the mapping apply/audit workflow (#525, epic #510).
///
/// <c>ProviderRoleAssignments</c> stores which provider fills each of the six mapping roles
/// (display metadata, episode structure, acquisition identity, progress tracking, artwork,
/// cross-reference IDs). Rows are keyed by (<c>MediaType</c>, <c>WorkId</c>, <c>Role</c>); an empty
/// <c>WorkId</c> is the global default and a work id is a per-work override. Nothing stored means the
/// built-in defaults show through, reproducing Jularr's behaviour today — there is no global
/// "canonical AniList id".
///
/// <c>AnimeMappingAuditEntries</c> is the append-only history of applied mapping changes (what
/// changed, when and by whom) surfaced on the Mapping Review page.
///
/// Both tables are accessed only through raw-ADO.NET stores (<c>ProviderRoleAssignmentStore</c>,
/// <c>MappingAuditStore</c>), never mapped as EF entities, so the EF model snapshot is unchanged and
/// <c>dotnet ef migrations has-pending-model-changes</c> stays clean.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260929150000_AddProviderRolesAndMappingAudit")]
public sealed class AddProviderRolesAndMappingAudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "ProviderRoleAssignments" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_ProviderRoleAssignments" PRIMARY KEY,
                "MediaType" TEXT NOT NULL,
                "WorkId" TEXT NOT NULL,
                "Role" TEXT NOT NULL,
                "Provider" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL
            );

            CREATE UNIQUE INDEX "IX_ProviderRoleAssignments_Scope"
                ON "ProviderRoleAssignments" ("MediaType", "WorkId", "Role");

            CREATE TABLE "AnimeMappingAuditEntries" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_AnimeMappingAuditEntries" PRIMARY KEY,
                "AnimeId" TEXT NOT NULL,
                "Action" TEXT NOT NULL,
                "Summary" TEXT NOT NULL,
                "Details" TEXT NOT NULL,
                "Actor" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL
            );

            CREATE INDEX "IX_AnimeMappingAuditEntries_Anime"
                ON "AnimeMappingAuditEntries" ("AnimeId", "CreatedAt");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE "AnimeMappingAuditEntries";
            DROP TABLE "ProviderRoleAssignments";
            """);
    }
}
