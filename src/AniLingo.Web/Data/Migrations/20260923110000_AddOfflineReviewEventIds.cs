using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260923110000_AddOfflineReviewEventIds")]
public sealed class AddOfflineReviewEventIds : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "ClientEventId",
            table: "Reviews",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Reviews_ProfileId_ClientEventId",
            table: "Reviews",
            columns: new[] { "ProfileId", "ClientEventId" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Reviews_ProfileId_ClientEventId",
            table: "Reviews");

        migrationBuilder.DropColumn(
            name: "ClientEventId",
            table: "Reviews");
    }
}
