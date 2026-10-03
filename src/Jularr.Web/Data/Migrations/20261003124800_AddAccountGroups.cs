using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261003124800_AddAccountGroups")]
public partial class AddAccountGroups : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AccountGroups",
            columns: table => new
            {
                Id = table.Column<string>(maxLength: 32, nullable: false),
                Name = table.Column<string>(maxLength: 80, nullable: false),
                NormalizedName = table.Column<string>(maxLength: 80, nullable: false),
                CreatedAt = table.Column<DateTime>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AccountGroups", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "AccountGroupMembers",
            columns: table => new
            {
                GroupId = table.Column<string>(maxLength: 32, nullable: false),
                AccountId = table.Column<string>(maxLength: 32, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_AccountGroupMembers",
                    x => new { x.GroupId, x.AccountId });

                table.ForeignKey(
                    name: "FK_AccountGroupMembers_AccountGroups_GroupId",
                    column: x => x.GroupId,
                    principalTable: "AccountGroups",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);

                table.ForeignKey(
                    name: "FK_AccountGroupMembers_OwnerAccounts_AccountId",
                    column: x => x.AccountId,
                    principalTable: "OwnerAccounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AccountGroups_NormalizedName",
            table: "AccountGroups",
            column: "NormalizedName",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AccountGroupMembers_AccountId",
            table: "AccountGroupMembers",
            column: "AccountId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AccountGroupMembers");
        migrationBuilder.DropTable(name: "AccountGroups");
    }
}
