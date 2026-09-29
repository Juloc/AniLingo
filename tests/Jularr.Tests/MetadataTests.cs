using Jularr.Web.Data;
using Jularr.Web.Data.Migrations;
using Jularr.Web.Features.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Jularr.Tests;

[TestClass]
public sealed class MetadataTests
{
    [TestMethod]
    public void PreferredTitleUsesEnglishThenRomajiThenNative()
    {
        Assert.AreEqual(
            "Frieren: Beyond Journey's End",
            AnimeMetadataTitles.Choose(
                "Frieren: Beyond Journey's End",
                "Sousou no Frieren",
                "葬送のフリーレン",
                "Local"));

        Assert.AreEqual(
            "Sousou no Frieren",
            AnimeMetadataTitles.Choose(
                null,
                "Sousou no Frieren",
                "葬送のフリーレン",
                "Local"));

        Assert.AreEqual(
            "Local",
            AnimeMetadataTitles.Choose(null, null, null, "Local"));
    }

    [TestMethod]
    public void AniListSearchResponseMapsCachedDisplayFieldsAndSkipsAdultMedia()
    {
        const string json = """
        {
          "data": {
            "Page": {
              "media": [
                {
                  "id": 154587,
                  "title": {
                    "romaji": "Sousou no Frieren",
                    "english": "Frieren: Beyond Journey's End",
                    "native": "葬送のフリーレン"
                  },
                  "description": "An elf mage returns after an adventure.",
                  "coverImage": {
                    "extraLarge": "https://img.example/frieren-large.jpg",
                    "large": "https://img.example/frieren.jpg"
                  },
                  "bannerImage": "https://img.example/frieren-banner.jpg",
                  "format": "TV",
                  "status": "FINISHED",
                  "season": "FALL",
                  "seasonYear": 2023,
                  "episodes": 28,
                  "duration": 24,
                  "isAdult": false
                },
                {
                  "id": 999999,
                  "title": { "romaji": "Hidden" },
                  "isAdult": true
                }
              ]
            }
          }
        }
        """;

        var results = AniListMetadataProvider.ParseSearchResponse(json);

        Assert.AreEqual(1, results.Count);
        var result = results[0];
        Assert.AreEqual("154587", result.ExternalId);
        Assert.AreEqual("Frieren: Beyond Journey's End", result.PreferredTitle);
        Assert.AreEqual("葬送のフリーレン", result.NativeTitle);
        Assert.AreEqual(28, result.EpisodeCount);
        Assert.AreEqual(24, result.EpisodeDurationMinutes);
        Assert.AreEqual("https://img.example/frieren.jpg", result.CoverImageUrl);
    }
}
