using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260925122000_AddLearningV2Settings")]
public sealed class AddLearningV2Settings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "LearningScopeModes" (
                "ProfileId" TEXT NOT NULL,
                "ScopeType" TEXT NOT NULL,
                "ScopeKey" TEXT NOT NULL,
                "ModeOverride" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "PK_LearningScopeModes"
                    PRIMARY KEY ("ProfileId", "ScopeType", "ScopeKey")
            );
            CREATE INDEX "IX_LearningScopeModes_Profile"
                ON "LearningScopeModes" ("ProfileId", "ScopeType");

            CREATE TABLE "LearningCapabilityOverrides" (
                "ProfileId" TEXT NOT NULL,
                "ScopeType" TEXT NOT NULL,
                "ScopeKey" TEXT NOT NULL,
                "Capability" TEXT NOT NULL,
                "IsEnabled" INTEGER NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "PK_LearningCapabilityOverrides"
                    PRIMARY KEY (
                        "ProfileId", "ScopeType", "ScopeKey", "Capability")
            );
            CREATE INDEX "IX_LearningCapabilityOverrides_Profile"
                ON "LearningCapabilityOverrides" (
                    "ProfileId", "ScopeType", "ScopeKey");

            -- Existing learners keep the behavior they already opted into.
            -- Profiles with no historic learning state remain absent and therefore
            -- resolve to Off by default.
            INSERT OR IGNORE INTO "LearningScopeModes" (
                "ProfileId", "ScopeType", "ScopeKey", "ModeOverride", "UpdatedAt")
            SELECT DISTINCT
                "ProfileId", 'Profile', '*', 'Study', CURRENT_TIMESTAMP
            FROM "UserTerms"
            WHERE "ProfileId" IS NOT NULL AND trim("ProfileId") <> '';

            INSERT OR IGNORE INTO "LearningScopeModes" (
                "ProfileId", "ScopeType", "ScopeKey", "ModeOverride", "UpdatedAt")
            SELECT DISTINCT
                "ProfileId", 'Profile', '*', 'Study', CURRENT_TIMESTAMP
            FROM "Reviews"
            WHERE "ProfileId" IS NOT NULL AND trim("ProfileId") <> '';

            INSERT OR IGNORE INTO "LearningScopeModes" (
                "ProfileId", "ScopeType", "ScopeKey", "ModeOverride", "UpdatedAt")
            SELECT DISTINCT
                "ProfileId", 'Profile', '*', 'Study', CURRENT_TIMESTAMP
            FROM "LearningPreferences"
            WHERE "ProfileId" IS NOT NULL AND trim("ProfileId") <> '';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE "LearningCapabilityOverrides";
            DROP TABLE "LearningScopeModes";
            """);
    }
}
