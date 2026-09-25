using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniLingo.Web.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260925021500_AddUiTranslationCatalog")]
public sealed class AddUiTranslationCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "UiLocales" (
                "Locale" TEXT NOT NULL CONSTRAINT "PK_UiLocales" PRIMARY KEY,
                "EnglishName" TEXT NOT NULL,
                "NativeName" TEXT NOT NULL,
                "Direction" TEXT NOT NULL,
                "IsEnabled" INTEGER NOT NULL DEFAULT 1,
                "IsSource" INTEGER NOT NULL DEFAULT 0,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL
            );

            CREATE TABLE "UiTranslationMessages" (
                "Key" TEXT NOT NULL CONSTRAINT "PK_UiTranslationMessages" PRIMARY KEY,
                "DefaultText" TEXT NOT NULL,
                "Feature" TEXT NOT NULL,
                "Surface" TEXT NOT NULL,
                "Description" TEXT NOT NULL,
                "Tone" TEXT NOT NULL,
                "MaxLength" INTEGER NULL,
                "PlaceholdersJson" TEXT NOT NULL,
                "DoNotTranslateJson" TEXT NOT NULL,
                "SourceHash" TEXT NOT NULL,
                "SourceVersion" INTEGER NOT NULL DEFAULT 1,
                "UpdatedAt" TEXT NOT NULL
            );

            CREATE TABLE "UiTranslations" (
                "Locale" TEXT NOT NULL,
                "MessageKey" TEXT NOT NULL,
                "Text" TEXT NOT NULL,
                "Status" TEXT NOT NULL,
                "SourceHash" TEXT NOT NULL,
                "Provider" TEXT NULL,
                "Model" TEXT NULL,
                "PromptVersion" TEXT NULL,
                "GeneratedAt" TEXT NULL,
                "ReviewedAt" TEXT NULL,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "PK_UiTranslations" PRIMARY KEY ("Locale", "MessageKey"),
                CONSTRAINT "FK_UiTranslations_UiLocales_Locale"
                    FOREIGN KEY ("Locale") REFERENCES "UiLocales" ("Locale") ON DELETE CASCADE,
                CONSTRAINT "FK_UiTranslations_UiTranslationMessages_MessageKey"
                    FOREIGN KEY ("MessageKey") REFERENCES "UiTranslationMessages" ("Key") ON DELETE CASCADE
            );
            CREATE INDEX "IX_UiTranslations_MessageKey"
                ON "UiTranslations" ("MessageKey");
            CREATE INDEX "IX_UiTranslations_Locale_Status"
                ON "UiTranslations" ("Locale", "Status");

            CREATE TABLE "UiProfileLocales" (
                "ProfileId" TEXT NOT NULL CONSTRAINT "PK_UiProfileLocales" PRIMARY KEY,
                "Locale" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_UiProfileLocales_UiLocales_Locale"
                    FOREIGN KEY ("Locale") REFERENCES "UiLocales" ("Locale") ON DELETE RESTRICT
            );
            CREATE INDEX "IX_UiProfileLocales_Locale"
                ON "UiProfileLocales" ("Locale");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE "UiProfileLocales";
            DROP TABLE "UiTranslations";
            DROP TABLE "UiTranslationMessages";
            DROP TABLE "UiLocales";
            """);
    }
}
