using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class MovieTvFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Movies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Title = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: true),
                    TmdbId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ImdbId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LibraryPath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Movies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TvSeries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Title = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: true),
                    TmdbId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TvdbId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LibraryPath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TvSeries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Movies_Key",
                table: "Movies",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TvSeries_Key",
                table: "TvSeries",
                column: "Key",
                unique: true);

            // AcquisitionAccessPolicies / AcquisitionRequests are raw-SQL tables (not in the EF model), created
            // by the baseline migration. On the canonical PostgreSQL database: drop the retired UserAddMode
            // column (#597 stopped reading it, #593/#594 owns the schema drop) and widen the "Kind" CHECK
            // constraints so the new movie/tv media types are accepted. The SQLite test shim keeps these
            // harmlessly unchanged: its narrower checks are never exercised for movie/tv there, and its
            // ALTER TABLE cannot drop a column that carries a CHECK constraint.
            if (IsPostgres(migrationBuilder))
            {
                migrationBuilder.Sql(@"ALTER TABLE ""AcquisitionAccessPolicies"" DROP COLUMN IF EXISTS ""UserAddMode"";");
                migrationBuilder.Sql(DropKindCheckSql("AcquisitionAccessPolicies"));
                migrationBuilder.Sql(AddKindCheckSql("AcquisitionAccessPolicies", WithVideoKinds));
                migrationBuilder.Sql(DropKindCheckSql("AcquisitionRequests"));
                migrationBuilder.Sql(AddKindCheckSql("AcquisitionRequests", WithVideoKinds));
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (IsPostgres(migrationBuilder))
            {
                migrationBuilder.Sql(DropKindCheckSql("AcquisitionRequests"));
                migrationBuilder.Sql(AddKindCheckSql("AcquisitionRequests", LegacyKinds));
                migrationBuilder.Sql(DropKindCheckSql("AcquisitionAccessPolicies"));
                migrationBuilder.Sql(AddKindCheckSql("AcquisitionAccessPolicies", LegacyKinds));
                migrationBuilder.Sql(
                    @"ALTER TABLE ""AcquisitionAccessPolicies"" ADD COLUMN IF NOT EXISTS ""UserAddMode"" TEXT NOT NULL DEFAULT 'request' CHECK (""UserAddMode"" IN ('disabled', 'request', 'automatic'));");
            }

            migrationBuilder.DropTable(
                name: "Movies");

            migrationBuilder.DropTable(
                name: "TvSeries");
        }

        private const string LegacyKinds = "'anime', 'manga', 'lightNovel', 'book'";
        private const string WithVideoKinds = "'anime', 'manga', 'lightNovel', 'book', 'movie', 'tv'";

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
