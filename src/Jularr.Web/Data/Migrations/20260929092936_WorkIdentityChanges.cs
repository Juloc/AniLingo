using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class WorkIdentityChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkIdentityChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangeType = table.Column<int>(type: "integer", nullable: false),
                    MediaType = table.Column<int>(type: "integer", nullable: false),
                    TargetWorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceWorkId = table.Column<Guid>(type: "uuid", nullable: true),
                    Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Actor = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkIdentityChanges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkIdentityChanges_CreatedAt",
                table: "WorkIdentityChanges",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WorkIdentityChanges_SourceWorkId",
                table: "WorkIdentityChanges",
                column: "SourceWorkId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkIdentityChanges_TargetWorkId",
                table: "WorkIdentityChanges",
                column: "TargetWorkId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkIdentityChanges");
        }
    }
}
