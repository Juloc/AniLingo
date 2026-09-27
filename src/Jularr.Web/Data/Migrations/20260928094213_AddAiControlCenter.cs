using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jularr.Web.Data.Migrations;

/// <summary>
/// AI control center (#422): the cached provider model catalogs (kept, and marked stale, when a
/// refresh fails) and restart-safe daily usage aggregates per profile, provider, model and operation.
/// Only counters are stored; prompts and responses are never persisted.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260928094213_AddAiControlCenter")]
public sealed class AddAiControlCenter : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "AiModelCatalogs" (
                "ProviderKey" TEXT NOT NULL CONSTRAINT "PK_AiModelCatalogs" PRIMARY KEY,
                "ModelsJson" TEXT NOT NULL,
                "Discovery" TEXT NOT NULL CHECK ("Discovery" IN ('unknown', 'supported', 'unsupported')),
                "FetchedAt" TEXT NULL,
                "LastAttemptAt" TEXT NULL,
                "LastError" TEXT NULL
            );

            CREATE TABLE "AiUsageDaily" (
                "ProfileId" TEXT NOT NULL,
                "Day" TEXT NOT NULL,
                "ProviderId" TEXT NOT NULL,
                "Model" TEXT NOT NULL,
                "Operation" TEXT NOT NULL,
                "Requests" INTEGER NOT NULL DEFAULT 0,
                "EstimatedRequests" INTEGER NOT NULL DEFAULT 0,
                "InputTokens" INTEGER NOT NULL DEFAULT 0,
                "CachedInputTokens" INTEGER NOT NULL DEFAULT 0,
                "OutputTokens" INTEGER NOT NULL DEFAULT 0,
                "ReasoningOutputTokens" INTEGER NOT NULL DEFAULT 0,
                "EstimatedInputTokens" INTEGER NOT NULL DEFAULT 0,
                "EstimatedOutputTokens" INTEGER NOT NULL DEFAULT 0,
                "ContextTokens" INTEGER NOT NULL DEFAULT 0,
                "CacheHits" INTEGER NOT NULL DEFAULT 0,
                "ResumedChunks" INTEGER NOT NULL DEFAULT 0,
                "Retries" INTEGER NOT NULL DEFAULT 0,
                "Failures" INTEGER NOT NULL DEFAULT 0,
                "Cancellations" INTEGER NOT NULL DEFAULT 0,
                "DurationMs" INTEGER NOT NULL DEFAULT 0,
                CONSTRAINT "PK_AiUsageDaily" PRIMARY KEY ("ProfileId", "Day", "ProviderId", "Model", "Operation")
            );

            CREATE INDEX "IX_AiUsageDaily_Day" ON "AiUsageDaily" ("Day");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE "AiUsageDaily";
            DROP TABLE "AiModelCatalogs";
            """);
    }
}
