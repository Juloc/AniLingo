using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations
{
    /// <summary>
    /// Admin → History: records which account started an operation (null for work the system started on
    /// its own) and indexes finished operations by the time they finished. The Operations table is managed
    /// with plain SQL, so the migration is too.
    /// </summary>
    public partial class OperationActorAndHistoryIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "Operations" ADD COLUMN IF NOT EXISTS "ActorProfileId" TEXT NULL;
                CREATE INDEX IF NOT EXISTS "IX_Operations_History_FinishedAtUtc"
                    ON "Operations" ((COALESCE("FinishedAtUtc", "UpdatedAtUtc")))
                    WHERE "Status" IN (3, 4, 5, 6);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_Operations_History_FinishedAtUtc";
                ALTER TABLE "Operations" DROP COLUMN IF EXISTS "ActorProfileId";
                """);
        }
    }
}
