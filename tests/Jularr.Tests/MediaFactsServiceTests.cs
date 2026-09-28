using Jularr.Web.Data;
using Jularr.Web.Features.Books;
using Jularr.Web.Features.Library;
using Jularr.Web.Features.MediaFacts;
using Jularr.Web.Features.Metadata;
using Jularr.Web.Features.Novels;
using Jularr.Web.Features.Subtitles;
using Jularr.Web.Ui;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Tests;

[TestClass]
public sealed class MediaFactsServiceTests
{
    private static readonly DateTime BaseTime = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task AnimeFactsCombineMetadataInventoryAndSidecarSubtitlesAsync()
    {
        await using var fixture = await EpisodeFlowFixture.CreateAsync();
        var anime = await fixture.AddAnimeAsync("akatsuki");
        var episode1 = await fixture.AddEpisodeAsync(anime, 1, 1);
        var episode2 = await fixture.AddEpisodeAsync(anime, 1, 2);
        var episode3 = await fixture.AddEpisodeAsync(anime, 1, 3);

        fixture.Db.AnimeMetadata.Add(new AnimeMetadata
        {
            AnimeId = anime.Id,
            Provider = "anilist",
            ExternalId = "1",
            PreferredTitle = "Akatsuki no Sora",
            Status = "RELEASING",
            SeasonYear = 2024,
            EpisodeCount = 12,
            EpisodeDurationMinutes = 24
        });
        await AddStreamsAsync(fixture.Db, episode1, ("jpn", MediaStreamKind.Audio), ("ger", MediaStreamKind.Audio), ("eng", MediaStreamKind.Subtitle));
        await AddStreamsAsync(fixture.Db, episode2, ("jpn", MediaStreamKind.Audio), ("eng", MediaStreamKind.Subtitle));
        await AddStreamsAsync(fixture.Db, episode3, ("jpn", MediaStreamKind.Audio));
        fixture.Db.SubtitleTracks.Add(new SubtitleTrack { EpisodeId = episode1.Id, Path = "a.de.srt", Language = "de", Format = "srt" });
        await fixture.Db.SaveChangesAsync();

        var facts = await new MediaFactsService(fixture.Db).GetAnimeFactsAsync(anime.Id, CancellationToken.None);

        Assert.AreEqual(MediaBannerKind.Anime, facts.Kind);
        Assert.AreEqual(MediaReleaseStatus.Ongoing, facts.Status);
        Assert.AreEqual(12, facts.PrimaryUnitCount, "The provider's stated total wins over the three locally known episodes.");
        Assert.AreEqual(1, facts.SecondaryUnitCount);
        Assert.AreEqual(24, facts.RuntimeMinutes);
        Assert.AreEqual(2024, facts.ReleaseYear);

        var ja = Single(facts.Languages, "ja", MediaFactsLanguageUsage.Audio);
        Assert.AreEqual(3, ja.AvailableUnits);
        Assert.AreEqual(3, ja.TotalUnits);
        Assert.AreEqual(MediaFactsCoverage.Complete, ja.Coverage);

        var de = Single(facts.Languages, "de", MediaFactsLanguageUsage.Audio);
        Assert.AreEqual(1, de.AvailableUnits);
        Assert.AreEqual(MediaFactsCoverage.Partial, de.Coverage);

        var en = Single(facts.Languages, "en", MediaFactsLanguageUsage.Subtitle);
        Assert.AreEqual(2, en.AvailableUnits);
        Assert.AreEqual(MediaFactsCoverage.Partial, en.Coverage);

        // Sidecar subtitle track: same completeness accounting as an embedded stream would get.
        var deSubtitle = Single(facts.Languages, "de", MediaFactsLanguageUsage.Subtitle);
        Assert.AreEqual(1, deSubtitle.AvailableUnits);
        Assert.AreEqual(3, deSubtitle.TotalUnits);
    }

    [TestMethod]
    public async Task AnimeFactsFallBackToLocalEpisodesAndNfoYearWithoutMetadataAsync()
    {
        await using var fixture = await EpisodeFlowFixture.CreateAsync();
        var anime = await fixture.AddAnimeAsync("bravo");
        await fixture.AddEpisodeAsync(anime, 1, 1);
        await fixture.AddEpisodeAsync(anime, 2, 1, withMedia: false);
        fixture.Db.AnimeLocalMetadata.Add(new AnimeLocalMetadata { AnimeId = anime.Id, Source = "nfo", Year = 2019 });
        await fixture.Db.SaveChangesAsync();

        var facts = await new MediaFactsService(fixture.Db).GetAnimeFactsAsync(anime.Id, CancellationToken.None);

        Assert.IsNull(facts.Status);
        Assert.AreEqual(2, facts.PrimaryUnitCount);
        Assert.AreEqual(2, facts.SecondaryUnitCount);
        Assert.IsNull(facts.RuntimeMinutes);
        Assert.AreEqual(2019, facts.ReleaseYear);
        Assert.AreEqual(0, facts.Languages.Count, "Neither episode has any stream or subtitle track.");
    }

    [TestMethod]
    public async Task UnknownAnimeYieldsEmptyFactsAsync()
    {
        await using var fixture = await EpisodeFlowFixture.CreateAsync();

        var facts = await new MediaFactsService(fixture.Db).GetAnimeFactsAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.IsFalse(facts.HasContent);
        Assert.AreEqual(0, facts.Languages.Count);
    }

    [TestMethod]
    public async Task MangaFactsCountVolumesAndTextCompletenessAsync()
    {
        await using var fixture = await EpisodeFlowFixture.CreateAsync();
        var seriesId = Guid.NewGuid();
        await InsertMangaSeriesAsync(fixture.Db, seriesId, "RELEASING");
        await InsertMangaChapterAsync(fixture.Db, seriesId, 1, volumeNumber: 1, pageCount: 20);
        await InsertMangaChapterAsync(fixture.Db, seriesId, 2, volumeNumber: 1, pageCount: 18);
        await InsertMangaChapterAsync(fixture.Db, seriesId, 3, volumeNumber: 2, pageCount: 22);
        await InsertMangaChapterAsync(fixture.Db, seriesId, 4, volumeNumber: null, pageCount: 0);

        var facts = await new MediaFactsService(fixture.Db).GetMangaFactsAsync(seriesId, CancellationToken.None);

        Assert.AreEqual(MediaBannerKind.Manga, facts.Kind);
        Assert.AreEqual(MediaReleaseStatus.Ongoing, facts.Status);
        Assert.AreEqual(4, facts.PrimaryUnitCount);
        Assert.AreEqual(2, facts.SecondaryUnitCount, "Only chapters 1-3 carry a volume number; two distinct volumes.");

        var text = Single(facts.Languages, "ja", MediaFactsLanguageUsage.Text);
        Assert.AreEqual(3, text.AvailableUnits, "Three of the four chapters actually have pages.");
        Assert.AreEqual(4, text.TotalUnits);
        Assert.AreEqual(MediaFactsCoverage.Partial, text.Coverage);
    }

    [TestMethod]
    public async Task UnknownMangaSeriesYieldsEmptyFactsAsync()
    {
        await using var fixture = await EpisodeFlowFixture.CreateAsync();

        var facts = await new MediaFactsService(fixture.Db).GetMangaFactsAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.IsFalse(facts.HasContent);
    }

    [TestMethod]
    public async Task NovelFactsCoverOriginalAndTranslatedChaptersAsync()
    {
        await using var fixture = await EpisodeFlowFixture.CreateAsync();
        var work = new NovelWork
        {
            SourceProvider = "narou",
            SourceKey = "n0000aa",
            SourceUrl = "https://ncode.syosetu.com/n0000aa/",
            Title = "Test Novel",
            MetadataStatus = "FINISHED"
        };
        fixture.Db.NovelWorks.Add(work);
        var volume1 = new NovelVolume { WorkId = work.Id, Number = 1, SourceKey = "v1" };
        var volume2 = new NovelVolume { WorkId = work.Id, Number = 2, SourceKey = "v2" };
        fixture.Db.NovelVolumes.AddRange(volume1, volume2);
        var chapter1 = new NovelChapter { WorkId = work.Id, VolumeId = volume1.Id, Number = 1, SourceUrl = "c1", Title = "Ch 1", OriginalText = "hello" };
        var chapter2 = new NovelChapter { WorkId = work.Id, VolumeId = volume1.Id, Number = 2, SourceUrl = "c2", Title = "Ch 2", OriginalText = "world" };
        var chapter3 = new NovelChapter { WorkId = work.Id, VolumeId = volume2.Id, Number = 3, SourceUrl = "c3", Title = "Ch 3", OriginalText = "" };
        fixture.Db.NovelChapters.AddRange(chapter1, chapter2, chapter3);
        fixture.Db.NovelTranslations.AddRange(
            new NovelTranslation { ChapterId = chapter1.Id, TargetLanguage = "de", ProviderId = "p", SourceHash = "h1", Text = "hallo" },
            new NovelTranslation { ChapterId = chapter1.Id, TargetLanguage = "en", ProviderId = "p", SourceHash = "h1", Text = "hi" },
            new NovelTranslation { ChapterId = chapter2.Id, TargetLanguage = "en", ProviderId = "p", SourceHash = "h2", Text = "world" });
        await fixture.Db.SaveChangesAsync();

        var facts = await new MediaFactsService(fixture.Db).GetNovelFactsAsync(work.Id, CancellationToken.None);

        Assert.AreEqual(MediaBannerKind.LightNovel, facts.Kind);
        Assert.AreEqual(MediaReleaseStatus.Finished, facts.Status);
        Assert.AreEqual(3, facts.PrimaryUnitCount);
        Assert.AreEqual(2, facts.SecondaryUnitCount);

        var original = Single(facts.Languages, "ja", MediaFactsLanguageUsage.Text);
        Assert.AreEqual(2, original.AvailableUnits, "Only chapters 1 and 2 have downloaded text; a web novel with no Format falls back to the household's content language.");
        Assert.AreEqual(3, original.TotalUnits);

        var german = Single(facts.Languages, "de", MediaFactsLanguageUsage.Text);
        Assert.AreEqual(1, german.AvailableUnits);
        Assert.AreEqual(MediaFactsCoverage.Partial, german.Coverage);

        var english = Single(facts.Languages, "en", MediaFactsLanguageUsage.Text);
        Assert.AreEqual(2, english.AvailableUnits);
    }

    [TestMethod]
    public async Task NovelFactsUseTheBookFormatLanguageAsTheSourceAsync()
    {
        await using var fixture = await EpisodeFlowFixture.CreateAsync();
        var work = new NovelWork
        {
            SourceProvider = "book-epub",
            SourceKey = "book-1",
            SourceUrl = "",
            Title = "Imported Book",
            Format = "EPUB:de"
        };
        fixture.Db.NovelWorks.Add(work);
        var volume = new NovelVolume { WorkId = work.Id, Number = 1, SourceKey = "v1" };
        fixture.Db.NovelVolumes.Add(volume);
        fixture.Db.NovelChapters.Add(
            new NovelChapter { WorkId = work.Id, VolumeId = volume.Id, Number = 1, SourceUrl = "c1", Title = "Ch 1", OriginalText = "hallo welt" });
        await fixture.Db.SaveChangesAsync();

        var facts = await new MediaFactsService(fixture.Db).GetNovelFactsAsync(work.Id, CancellationToken.None);

        var german = Single(facts.Languages, "de", MediaFactsLanguageUsage.Text);
        Assert.AreEqual(1, german.AvailableUnits);
        Assert.AreEqual(MediaFactsCoverage.Complete, german.Coverage);
    }

    [TestMethod]
    public void BookCatalogFactsGroupKnownEditionsByLanguage()
    {
        var book = new BookCatalogItem(
            "gutenberg-1",
            "A Book",
            "An Author",
            null,
            null,
            [],
            1954,
            null,
            null,
            "https://example.test",
            "Project Gutenberg",
            null)
        {
            Editions =
            [
                new BookEditionSummary("e1", 1954, "en", null, null, "epub", "Project Gutenberg", null),
                new BookEditionSummary("e2", 1960, "en", null, null, "epub", "Open Library", null),
                new BookEditionSummary("e3", 1970, "de", null, null, "epub", "Open Library", null)
            ]
        };

        var facts = MediaFactsService.CreateBookCatalogFacts(book);

        Assert.AreEqual(MediaBannerKind.Book, facts.Kind);
        Assert.IsNull(facts.Status);
        Assert.AreEqual(1954, facts.ReleaseYear);

        var en = Single(facts.Languages, "en", MediaFactsLanguageUsage.Text);
        Assert.AreEqual(2, en.AvailableUnits);
        Assert.AreEqual(3, en.TotalUnits);
        Assert.AreEqual(MediaFactsCoverage.Partial, en.Coverage);

        var de = Single(facts.Languages, "de", MediaFactsLanguageUsage.Text);
        Assert.AreEqual(1, de.AvailableUnits);
    }

    [TestMethod]
    public void BookCatalogFactsFallBackToTheRecordsOwnLanguageWithoutMergedEditions()
    {
        var book = new BookCatalogItem(
            "ol-1",
            "Raw Result",
            null,
            null,
            null,
            [],
            null,
            null,
            null,
            "https://example.test",
            "Open Library",
            null)
        {
            Language = "id"
        };

        var facts = MediaFactsService.CreateBookCatalogFacts(book);

        var id = Single(facts.Languages, "id", MediaFactsLanguageUsage.Text);
        Assert.AreEqual(1, id.AvailableUnits);
        Assert.AreEqual(1, id.TotalUnits);
        Assert.AreEqual(MediaFactsCoverage.Complete, id.Coverage);
    }

    [TestMethod]
    public void BookCatalogFactsAreEmptyWithoutAnyKnownLanguage()
    {
        var book = new BookCatalogItem(
            "ol-2",
            "No Language Info",
            null,
            null,
            null,
            [],
            2001,
            null,
            null,
            "https://example.test",
            "Open Library",
            null);

        var facts = MediaFactsService.CreateBookCatalogFacts(book);

        Assert.AreEqual(0, facts.Languages.Count);
        Assert.AreEqual(2001, facts.ReleaseYear);
    }

    private static MediaFactsLanguageRow Single(
        IReadOnlyList<MediaFactsLanguageRow> rows,
        string language,
        MediaFactsLanguageUsage usage) =>
        rows.Single(x => x.Language == language && x.Usage == usage);

    private static async Task AddStreamsAsync(
        AppDbContext db,
        Episode episode,
        params (string Language, MediaStreamKind Kind)[] streams)
    {
        var mediaFileId = await db.MediaFiles
            .Where(x => x.EpisodeId == episode.Id)
            .Select(x => x.Id)
            .SingleAsync();
        db.MediaAnalyses.Add(new MediaAnalysis
        {
            MediaFileId = mediaFileId,
            Status = MediaAnalysisStatus.Succeeded,
            ProbeVersion = MediaInventoryService.CurrentProbeVersion,
            SourceSizeBytes = 1,
            SourceLastWriteTimeUtc = BaseTime
        });
        for (var index = 0; index < streams.Length; index++)
        {
            db.MediaAnalysisStreams.Add(new MediaAnalysisStream
            {
                MediaFileId = mediaFileId,
                StreamIndex = index + 1,
                Kind = streams[index].Kind,
                Codec = streams[index].Kind == MediaStreamKind.Audio ? "aac" : "ass",
                Language = streams[index].Language
            });
        }

        await db.SaveChangesAsync();
    }

    private static Task InsertMangaSeriesAsync(AppDbContext db, Guid id, string? status) =>
        db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "MangaSeries"
                ("Id", "Title", "SourcePath", "MetadataStatus", "Direction", "CreatedAt", "UpdatedAt")
            VALUES ({0}, {1}, {2}, {3}, 'rtl', {4}, {4});
            """,
            id.ToString(),
            "Test Series",
            $"/data/manga/{id}",
            status!,
            BaseTime.ToString("O"));

    private static Task InsertMangaChapterAsync(
        AppDbContext db,
        Guid seriesId,
        double number,
        int? volumeNumber,
        int pageCount) =>
        db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "MangaChapters"
                ("Id", "SeriesId", "Number", "VolumeNumber", "Title", "SourcePath", "SourceKind", "PageCount", "SourceUpdatedAt", "CreatedAt", "UpdatedAt")
            VALUES ({0}, {1}, {2}, {3}, {4}, {5}, 'cbz', {6}, {7}, {7}, {7});
            """,
            Guid.NewGuid().ToString(),
            seriesId.ToString(),
            number,
            volumeNumber!,
            $"Chapter {number}",
            $"/data/manga/{seriesId}/{number}.cbz",
            pageCount,
            BaseTime.ToString("O"));
}
