using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations
{
    /// <summary>
    /// Admin → Activity: how soon queued work should run (1 low, 2 normal, 3 high); everything that
    /// already exists is normal. The Operations table is managed with plain SQL, so the migration is too.
    /// </summary>
    public partial class AddOperationPriority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "Operations" ADD COLUMN IF NOT EXISTS "Priority" bigint NOT NULL DEFAULT 2;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "Operations" DROP COLUMN IF EXISTS "Priority";
                """);
        }
    }
}
