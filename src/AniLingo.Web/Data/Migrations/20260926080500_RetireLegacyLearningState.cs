using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

/// <summary>
/// Drops the legacy per-profile learning tables after
/// <see cref="DatabaseMigrationBridge"/> has converted them into Learning cards.
/// The guard aborts the migration (and its transaction) when UserTerms rows were
/// not converted, so applying migrations without the bridge can never discard
/// learning progress.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260926080500_RetireLegacyLearningState")]
public sealed class RetireLegacyLearningState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TEMP TABLE "LegacyLearningStateMustBeConvertedByDatabaseMigrationBridge" (
                "UnconvertedUserTerms" INTEGER NOT NULL CHECK ("UnconvertedUserTerms" = 0)
            );
            INSERT INTO "LegacyLearningStateMustBeConvertedByDatabaseMigrationBridge" ("UnconvertedUserTerms")
            SELECT COUNT(*)
            FROM "UserTerms"
            WHERE "Id" NOT IN (SELECT "Id" FROM "LearningCards");
            DROP TABLE "LegacyLearningStateMustBeConvertedByDatabaseMigrationBridge";

            DROP TABLE "Reviews";
            DROP TABLE "UserTerms";
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException(
            "Legacy UserTerms/Reviews were converted into Learning cards; this one-way migration cannot be reverted.");
    }
}
