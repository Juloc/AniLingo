using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AudiobookFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Audiobooks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Title = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: true),
                    Author = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Narrator = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Asin = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    ChapterCount = table.Column<int>(type: "integer", nullable: true),
                    LibraryPath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Audiobooks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AudiobookFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AudiobookId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FileName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Format = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AudiobookFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AudiobookFiles_Audiobooks_AudiobookId",
                        column: x => x.AudiobookId,
                        principalTable: "Audiobooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AudiobookProgress",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    AudiobookId = table.Column<Guid>(type: "uuid", nullable: false),
                    PositionMs = table.Column<long>(type: "bigint", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    ChapterNumber = table.Column<int>(type: "integer", nullable: false),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AudiobookProgress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AudiobookProgress_Audiobooks_AudiobookId",
                        column: x => x.AudiobookId,
                        principalTable: "Audiobooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AudiobookFiles_AudiobookId_FileKey",
                table: "AudiobookFiles",
                columns: new[] { "AudiobookId", "FileKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AudiobookProgress_AudiobookId",
                table: "AudiobookProgress",
                column: "AudiobookId");

            migrationBuilder.CreateIndex(
                name: "IX_AudiobookProgress_ProfileId_AudiobookId",
                table: "AudiobookProgress",
                columns: new[] { "ProfileId", "AudiobookId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AudiobookProgress_ProfileId_UpdatedAt",
                table: "AudiobookProgress",
                columns: new[] { "ProfileId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Audiobooks_Key",
                table: "Audiobooks",
                column: "Key",
                unique: true);

            // AcquisitionAccessPolicies / AcquisitionRequests are raw-SQL tables (not in the EF model),
            // created by the baseline migration and last widened for movie/tv by the #593/#594 migration.
            // On the canonical PostgreSQL database, widen the "Kind" CHECK constraints again so the new
            // audiobook media type is accepted (#440). The SQLite import shim keeps its narrower checks
            // harmlessly unchanged; they are never exercised for audiobook there.
            if (IsPostgres(migrationBuilder))
            {
                migrationBuilder.Sql(DropKindCheckSql("AcquisitionAccessPolicies"));
                migrationBuilder.Sql(AddKindCheckSql("AcquisitionAccessPolicies", WithAudiobookKinds));
                migrationBuilder.Sql(DropKindCheckSql("AcquisitionRequests"));
                migrationBuilder.Sql(AddKindCheckSql("AcquisitionRequests", WithAudiobookKinds));
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (IsPostgres(migrationBuilder))
            {
                migrationBuilder.Sql(DropKindCheckSql("AcquisitionRequests"));
                migrationBuilder.Sql(AddKindCheckSql("AcquisitionRequests", VideoKinds));
                migrationBuilder.Sql(DropKindCheckSql("AcquisitionAccessPolicies"));
                migrationBuilder.Sql(AddKindCheckSql("AcquisitionAccessPolicies", VideoKinds));
            }

            migrationBuilder.DropTable(
                name: "AudiobookFiles");

            migrationBuilder.DropTable(
                name: "AudiobookProgress");

            migrationBuilder.DropTable(
                name: "Audiobooks");
        }

        private const string VideoKinds = "'anime', 'manga', 'lightNovel', 'book', 'movie', 'tv'";
        private const string WithAudiobookKinds = "'anime', 'manga', 'lightNovel', 'book', 'movie', 'tv', 'audiobook'";

        private static bool IsPostgres(MigrationBuilder migrationBuilder) =>
            migrationBuilder.ActiveProvider?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

        // Drops whatever CHECK constraint currently guards the raw table's "Kind" column, whatever its
        // (auto-generated) name, so the widen/narrow is name-agnostic and safe to run on any existing database.
        private static string DropKindCheckSql(string table) =>
            $@"DO $$
DECLARE constraint_name text;
BEGIN
    FOR constraint_name IN
        SELECT con.conname
        FROM pg_constraint con
        JOIN pg_class rel ON rel.oid = con.conrelid
        WHERE rel.relname = '{table}'
          AND con.contype = 'c'
          AND pg_get_constraintdef(con.oid) ILIKE '%""Kind""%'
    LOOP
        EXECUTE format('ALTER TABLE ""{table}"" DROP CONSTRAINT %I', constraint_name);
    END LOOP;
END $$;";

        private static string AddKindCheckSql(string table, string kinds) =>
            $@"ALTER TABLE ""{table}"" ADD CONSTRAINT ""CK_{table}_Kind"" CHECK (""Kind"" IN ({kinds}));";
    }
}
