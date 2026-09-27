using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260928113000_AddFranchiseRelations")]
public sealed class AddFranchiseRelations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MediaRelations",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                FromMediaType = table.Column<string>(type: "TEXT", nullable: false),
                FromProvider = table.Column<string>(type: "TEXT", nullable: false),
                FromExternalId = table.Column<string>(type: "TEXT", nullable: false),
                ToMediaType = table.Column<string>(type: "TEXT", nullable: false),
                ToProvider = table.Column<string>(type: "TEXT", nullable: false),
                ToExternalId = table.Column<string>(type: "TEXT", nullable: false),
                RelationType = table.Column<string>(type: "TEXT", nullable: false),
                Source = table.Column<string>(type: "TEXT", nullable: false),
                Confidence = table.Column<double>(type: "REAL", nullable: false),
                ReviewState = table.Column<string>(type: "TEXT", nullable: false),
                IsManual = table.Column<int>(type: "INTEGER", nullable: false),
                UpdatedAtUtc = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MediaRelations", x => x.Id);
                table.UniqueConstraint(
                    "AK_MediaRelations_Direction",
                    x => new
                    {
                        x.FromMediaType,
                        x.FromProvider,
                        x.FromExternalId,
                        x.ToMediaType,
                        x.ToProvider,
                        x.ToExternalId,
                        x.RelationType
                    });
            });

        migrationBuilder.CreateIndex(
            name: "IX_MediaRelations_From",
            table: "MediaRelations",
            columns: new[] { "FromMediaType", "FromProvider", "FromExternalId" });

        migrationBuilder.CreateIndex(
            name: "IX_MediaRelations_To",
            table: "MediaRelations",
            columns: new[] { "ToMediaType", "ToProvider", "ToExternalId" });

        migrationBuilder.CreateIndex(
            name: "IX_MediaRelations_ReviewState",
            table: "MediaRelations",
            column: "ReviewState");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MediaRelations");
    }
}
