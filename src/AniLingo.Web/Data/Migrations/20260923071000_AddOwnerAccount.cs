using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260923071000_AddOwnerAccount")]
public sealed class AddOwnerAccount : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "OwnerAccounts",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                UserName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                NormalizedUserName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                PasswordHash = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OwnerAccounts", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_OwnerAccounts_NormalizedUserName",
            table: "OwnerAccounts",
            column: "NormalizedUserName",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "OwnerAccounts");
}
