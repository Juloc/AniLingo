using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

/// <summary>
/// Offline sync state for Book/Novel bookmarks (#221 part 1): a last-writer-wins
/// clock (<c>SyncUpdatedAt</c>) and idempotency stamp (<c>ClientEventId</c>) on
/// <c>NovelBookmarks</c>, plus tombstones of bookmarks removed offline so a
/// stale replayed add/edit can never resurrect one removed later. Reading
/// progress reconciliation reuses the existing <c>NovelProgress</c> table.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260926144053_AddOfflineLibrarySync")]
public sealed class AddOfflineLibrarySync : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "ClientEventId",
            table: "NovelBookmarks",
            type: "TEXT",
            nullable: true);

        // A safely old constant (SQLite ADD COLUMN cannot default to another
        // column): existing bookmarks predate offline sync, so any real
        // client timestamp is newer and wins under last-writer-wins.
        migrationBuilder.AddColumn<DateTime>(
            name: "SyncUpdatedAt",
            table: "NovelBookmarks",
            type: "TEXT",
            nullable: false,
            defaultValue: new DateTime(1, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        migrationBuilder.CreateTable(
            name: "NovelBookmarkTombstones",
            columns: table => new
            {
                BookmarkId = table.Column<Guid>(type: "TEXT", nullable: false),
                ProfileId = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                WorkId = table.Column<Guid>(type: "TEXT", nullable: false),
                DeletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                RecordedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NovelBookmarkTombstones", x => x.BookmarkId);
                table.ForeignKey(
                    name: "FK_NovelBookmarkTombstones_NovelWorks_WorkId",
                    column: x => x.WorkId,
                    principalTable: "NovelWorks",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_NovelBookmarkTombstones_ProfileId_WorkId",
            table: "NovelBookmarkTombstones",
            columns: new[] { "ProfileId", "WorkId" });

        migrationBuilder.CreateIndex(
            name: "IX_NovelBookmarkTombstones_WorkId",
            table: "NovelBookmarkTombstones",
            column: "WorkId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "NovelBookmarkTombstones");
        migrationBuilder.DropColumn(name: "ClientEventId", table: "NovelBookmarks");
        migrationBuilder.DropColumn(name: "SyncUpdatedAt", table: "NovelBookmarks");
    }
}
