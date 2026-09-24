using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260924184000_AddEpisodeProgress")]
public sealed class AddEpisodeProgress : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "EpisodeProgress" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_EpisodeProgress" PRIMARY KEY,
                "ProfileId" TEXT NOT NULL,
                "EpisodeId" TEXT NOT NULL,
                "PositionMs" INTEGER NOT NULL,
                "DurationMs" INTEGER NULL,
                "IsCompleted" INTEGER NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_EpisodeProgress_Episodes_EpisodeId"
                    FOREIGN KEY ("EpisodeId") REFERENCES "Episodes" ("Id") ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX "IX_EpisodeProgress_ProfileId_EpisodeId"
                ON "EpisodeProgress" ("ProfileId", "EpisodeId");
            CREATE INDEX "IX_EpisodeProgress_ProfileId_UpdatedAt"
                ON "EpisodeProgress" ("ProfileId", "UpdatedAt");
            CREATE INDEX "IX_EpisodeProgress_EpisodeId"
                ON "EpisodeProgress" ("EpisodeId");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE "EpisodeProgress";
            """);
    }
}
