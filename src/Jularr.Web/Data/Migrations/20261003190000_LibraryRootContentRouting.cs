using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class LibraryRootContentRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Preserve the historical behavior of existing roots. New LibraryRoot objects default
            // to HardlinkOrCopy in application code, but upgraded rows remain Move until the owner changes them.
            migrationBuilder.AddColumn<int>(
                name: "PlacementPolicy",
                table: "LibraryRoots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "LibraryRootContentAssignments",
                columns: table => new
                {
                    LibraryRootId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentType = table.Column<int>(type: "integer", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_LibraryRootContentAssignments",
                        x => new { x.LibraryRootId, x.ContentType });
                    table.ForeignKey(
                        name: "FK_LibraryRootContentAssignments_LibraryRoots_LibraryRootId",
                        column: x => x.LibraryRootId,
                        principalTable: "LibraryRoots",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryRootContentAssignments_ContentType_IsDefault",
                table: "LibraryRootContentAssignments",
                columns: new[] { "ContentType", "IsDefault" });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryRootContentAssignments_ContentType",
                table: "LibraryRootContentAssignments",
                column: "ContentType",
                unique: true,
                filter: "\"IsDefault\" = TRUE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "LibraryRootContentAssignments");

            migrationBuilder.DropColumn(
                name: "PlacementPolicy",
                table: "LibraryRoots");
        }
    }
}
