using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260922183300_AddLearningPreferences")]
public sealed class AddLearningPreferences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "LearningPreferences",
            columns: table => new
            {
                ProfileId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                DesiredRetention = table.Column<double>(type: "REAL", nullable: false),
                ReviewBatchSize = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LearningPreferences", x => x.ProfileId);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "LearningPreferences");
}
