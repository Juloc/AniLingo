using AniLingo.Web.Data;
using AniLingo.Web.Features.Books;
using AniLingo.Web.Features.MediaMapping;
using AniLingo.Web.Features.Metadata;
using AniLingo.Web.Features.Novels;
using AniLingo.Web.Features.Tracking;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace AniLingo.Tests;

/// <summary>
/// Series → volume → chapter model: EPUB light-novel volumes and web novels
/// share the canonical Novel catalog, progress and annotation services.
/// </summary>
[TestClass]
public sealed class NovelEpubVolumeTests
{
    private const string Series = "テストシリーズ";

    [TestMethod]
    public async Task MultipleEpubVolumesFormOneSeriesInVolumeOrder()
    {
        await using var fixture = await Fixture.CreateAsync();

        // Volume 2 arrives first; volume 1 must still be read first.
        var second = await fixture.ImportAsync(Volume(2, "第二巻の始まり", "第二巻の続き"));
        var first = await fixture.ImportAsync(Volume(1, "第一巻の始まり", "第一巻の続き"));

        Assert.IsTrue(second.Succeeded, second.Message);
        Assert.IsTrue(first.Succeeded, first.Message);
        Assert.AreEqual(first.WorkId, second.WorkId);

        var work = await fixture.Db.NovelWorks.SingleAsync();
        Assert.AreEqual(NovelEpubImportService.Provider, work.SourceProvider);
        Assert.AreEqual(Series, work.Title);

        var detail = (await fixture.Catalog.GetWorkDetailAsync(work.Id, CancellationToken.None))!;
        CollectionAssert.AreEqual(new[] { 1, 2 }, detail.Volumes.Select(x => x.Number).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, detail.Chapters.Select(x => x.Number).ToArray());
        Assert.IsTrue(detail.Chapters.Take(2).All(x => x.VolumeId == detail.Volumes[0].Id));
        Assert.IsTrue(detail.Chapters.Skip(2).All(x => x.VolumeId == detail.Volumes[1].Id));
        Assert.AreEqual("第一巻", detail.Chapters[0].Title);

        var library = (await fixture.Catalog.GetLibraryAsync("reader-a", CancellationToken.None)).Single();
        Assert.AreEqual(2, library.VolumeCount);
        StringAssert.StartsWith(library.CoverImageUrl, $"/Novels/Asset/{detail.Volumes[0].Id:N}/");

        var crossing = (await fixture.Catalog.GetReaderChapterAsync(detail.Chapters[2].Id, CancellationToken.None))!;
        Assert.AreEqual(2, crossing.VolumeNumber);
        Assert.IsTrue(crossing.IsEpubVolume);
        Assert.AreEqual(detail.Chapters[1].Id, crossing.PreviousChapterId);
    }

    [TestMethod]
    public async Task ReplacingAnEpubRefreshesChangedChaptersAndKeepsNotesAndProgress()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ImportAsync(Volume(1, "最初の章です。", "二番目の章です。", "三番目の章です。"));
        await fixture.ImportAsync(Volume(2, "第二巻の一章です。", "第二巻の二章です。"));

        var before = await fixture.Db.NovelChapters.AsNoTracking().OrderBy(x => x.Number).ToListAsync();
        var progress = new NovelProgressService(fixture.Db);
        var annotations = new NovelAnnotationService(fixture.Db);
        await progress.SaveProgressAsync("reader-a", before[3].Id, 500, "ja", 1, 2, CancellationToken.None);
        await progress.SaveProgressAsync("reader-b", before[1].Id, 250, "ja", 0, 0, CancellationToken.None);
        var bookmark = await annotations.AddBookmarkAsync(
            "reader-a", before[0].Id, 100, "ja", 1, 1, "mark", null, null, CancellationToken.None);
        var highlight = await annotations.AddHighlightAsync(
            "reader-a", before[2].Id, "ja", 1, 0, 3, null, CancellationToken.None);

        var unchanged = await fixture.ImportAsync(Volume(1, "最初の章です。", "二番目の章です。", "三番目の章です。"));
        StringAssert.Contains(unchanged.Message, "already up to date");

        // Replacement file: same identity, chapter 3 corrected, a chapter added.
        var replaced = await fixture.ImportAsync(
            Volume(1, "最初の章です。", "二番目の章です。", "三番目の章を直しました。", "新しい四番目の章です。"));
        Assert.IsTrue(replaced.Succeeded, replaced.Message);
        StringAssert.Contains(replaced.Message, "1 new, 1 updated, 0 removed, 2 unchanged");

        var after = await fixture.Db.NovelChapters.AsNoTracking().OrderBy(x => x.Number).ToListAsync();
        Assert.AreEqual(2, await fixture.Db.NovelVolumes.CountAsync());
        Assert.AreEqual(6, after.Count);
        CollectionAssert.AreEqual(
            new[] { before[0].Id, before[1].Id, before[2].Id },
            after.Take(3).Select(x => x.Id).ToArray());
        Assert.AreEqual(before[0].UpdatedAt, after[0].UpdatedAt);
        StringAssert.Contains(after[2].OriginalText, "直しました");
        Assert.AreNotEqual(before[2].SourceHash, after[2].SourceHash);
        StringAssert.Contains(after[3].OriginalText, "四番目");

        // Volume 2 moved behind the new chapter without losing its identity.
        Assert.AreEqual(before[3].Id, after[4].Id);
        Assert.AreEqual(5, after[4].Number);

        var progressRows = await fixture.Db.NovelProgress.AsNoTracking().ToDictionaryAsync(x => x.ProfileId);
        Assert.AreEqual(before[3].Id, progressRows["reader-a"].ChapterId);
        Assert.AreEqual(before[1].Id, progressRows["reader-b"].ChapterId);
        Assert.IsTrue(await fixture.Db.NovelBookmarks.AnyAsync(x => x.Id == bookmark.Id));
        Assert.IsTrue(await fixture.Db.NovelHighlights.AnyAsync(x => x.Id == highlight.Id));
    }

    [TestMethod]
    public async Task ProgressAndAnnotationsStayPerProfileAcrossVolumes()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ImportAsync(Volume(1, "第一巻の本文です。"));
        await fixture.ImportAsync(Volume(2, "第二巻の本文です。"));
        var chapters = await fixture.Db.NovelChapters.AsNoTracking().OrderBy(x => x.Number).ToListAsync();

        var progress = new NovelProgressService(fixture.Db);
        var annotations = new NovelAnnotationService(fixture.Db);
        await progress.SaveProgressAsync("reader-a", chapters[1].Id, 300, "ja", 0, 0, CancellationToken.None);
        await progress.SaveProgressAsync("reader-b", chapters[0].Id, 800, "ja", 0, 0, CancellationToken.None);
        await annotations.AddBookmarkAsync(
            "reader-a", chapters[1].Id, 300, "ja", 0, 0, null, null, null, CancellationToken.None);

        var libraryA = (await fixture.Catalog.GetLibraryAsync("reader-a", CancellationToken.None)).Single();
        var libraryB = (await fixture.Catalog.GetLibraryAsync("reader-b", CancellationToken.None)).Single();
        Assert.AreEqual(2, libraryA.CurrentVolumeNumber);
        Assert.AreEqual(chapters[1].Id, libraryA.CurrentChapterId);
        Assert.AreEqual(1, libraryB.CurrentVolumeNumber);
        Assert.AreEqual(800, libraryB.ProgressPermille);

        var notesA = await annotations.GetChapterAnnotationsAsync(
            "reader-a", chapters[1].WorkId, chapters[1].Id, CancellationToken.None);
        var notesB = await annotations.GetChapterAnnotationsAsync(
            "reader-b", chapters[1].WorkId, chapters[1].Id, CancellationToken.None);
        Assert.AreEqual(1, notesA.Bookmarks.Count);
        Assert.AreEqual(0, notesB.Bookmarks.Count);
    }

    [TestMethod]
    public void EpubChapterXhtmlIsSanitizedWithRubyEmphasisAndIllustrations()
    {
        var epub = new EpubTestBuilder { Title = "Sanitize" }
            .Image("images/ill.png")
            .Image("images/ill2.png", EpubTestBuilder.Png.Append((byte)0).ToArray())
            .Image("images/vector.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>x()</script></svg>"u8.ToArray(), "image/svg+xml")
            .RawDocument("text/ch1.xhtml", """
                <?xml version="1.0" encoding="utf-8"?>
                <!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.1//EN" "http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd">
                <html xmlns="http://www.w3.org/1999/xhtml" xmlns:xlink="http://www.w3.org/1999/xlink">
                <head><title>t</title><style>p { color: red; }</style><script>alert('head')</script></head>
                <body onload="evil()">
                  <h2>第一章&nbsp;出会い</h2>
                  <script>alert('body')</script>
                  <p onclick="evil()" style="x">本文の<ruby>漢字<rp>(</rp><rt>かんじ</rt><rp>)</rp></ruby>を<em>強調</em>し<b>太字</b>で&lt;b&gt;と書く。</p>
                  <p><span class="em-sesame">傍点</span>付きの文と<a href="javascript:alert(1)">リンク</a>です。</p>
                  <p><img src="http://evil.example/track.png" alt="x"/><img src="data:image/png;base64,AAAA"/><img src="../images/vector.svg"/></p>
                  <p><img src="../images/ill.png" alt="挿絵"/></p>
                  <div><svg><image xlink:href="../images/ill2.png"/></svg></div>
                  <iframe src="https://evil.example/"></iframe>
                  <p>最後の段落です。</p>
                </body></html>
                """)
            .Build();

        var parsed = EpubBookParser.Parse(epub, "sanitize.epub", includeAssets: true);
        var chapter = parsed.Chapters.Single();
        var blocks = chapter.Blocks;

        Assert.AreEqual("第一章 出会い", chapter.Title);
        var text = chapter.Text;
        Assert.IsFalse(text.Contains("alert", StringComparison.Ordinal), text);
        Assert.IsFalse(text.Contains("かんじ", StringComparison.Ordinal), "Ruby readings are not part of the plain text.");
        Assert.IsFalse(text.Contains("color", StringComparison.Ordinal), text);
        StringAssert.Contains(text, "本文の漢字を強調し太字で<b>と書く。");

        // Only internal raster images survive: no http:, data: or SVG documents.
        var internalImages = new[] { "OEBPS/images/ill.png", "OEBPS/images/ill2.png" };
        CollectionAssert.AreEqual(
            internalImages,
            blocks.Where(x => x.Kind == NovelContentBlock.ImageKind).Select(x => x.Source).ToArray());
        CollectionAssert.AreEqual(internalImages, parsed.Assets.Select(x => x.Path).ToArray());
        Assert.AreEqual("挿絵", blocks.First(x => x.Kind == NovelContentBlock.ImageKind).Alt);

        var paragraph = blocks.Single(x => x.PlainText.StartsWith("本文", StringComparison.Ordinal));
        Assert.IsTrue(paragraph.Runs!.Any(x => x is { Text: "漢字", Ruby: "かんじ" }));
        Assert.IsTrue(paragraph.Runs!.Any(x => x is { Text: "強調", Emphasis: true }));
        Assert.IsTrue(paragraph.Runs!.Any(x => x is { Text: "太字", Strong: true }));
        Assert.IsTrue(blocks.Single(x => x.PlainText.StartsWith("傍点", StringComparison.Ordinal))
            .Runs!.Any(x => x is { Text: "傍点", Emphasis: true }));

        var html = NovelChapterDocument.RenderRuns(paragraph.Runs!).ToString()!;
        StringAssert.Contains(html, "<ruby>漢字<rt data-rt=\"かんじ\"></rt></ruby>");
        StringAssert.Contains(html, "<em>強調</em>");
        StringAssert.Contains(html, "<strong>太字</strong>");
        StringAssert.Contains(html, "&lt;b&gt;");
        Assert.IsFalse(html.Contains("onclick", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(html.Contains("<script", StringComparison.OrdinalIgnoreCase));

        // Rendered text blocks line up with the plain paragraphs anchors use.
        var readerBlocks = NovelChapterDocument.BuildReaderBlocks(
            chapter.Text,
            NovelChapterDocument.Serialize(blocks));
        var paragraphs = NovelTextLayout.SplitParagraphs(chapter.Text);
        var textBlocks = readerBlocks.Where(x => !x.IsImage).ToArray();
        Assert.AreEqual(paragraphs.Count, textBlocks.Length);
        for (var index = 0; index < paragraphs.Count; index++)
        {
            Assert.AreEqual(index, textBlocks[index].ParagraphIndex);
            Assert.AreEqual(paragraphs[index], string.Concat(textBlocks[index].Runs.Select(x => x.Text)));
        }

        Assert.IsTrue(textBlocks[0].IsHeading);
    }

    [TestMethod]
    public async Task ImportedIllustrationsAreCachedAsContentAddressedAssets()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Volume(1, "挿絵のある本文です。");
        builder.RawChapter("text/ill.xhtml", "<p><img src=\"../images/inline.png\"/></p>");
        builder.Chapter("text/after.xhtml", "続き", "挿絵の後の本文です。");
        builder.Image("images/inline.png");

        var outcome = await fixture.ImportAsync(builder);
        Assert.IsTrue(outcome.Succeeded, outcome.Message);

        var volume = await fixture.Db.NovelVolumes.SingleAsync();
        var chapters = await fixture.Db.NovelChapters.AsNoTracking().OrderBy(x => x.Number).ToListAsync();
        Assert.AreEqual(2, chapters.Count, "An illustration-only page opens the next text chapter.");

        var image = NovelChapterDocument.Deserialize(chapters[1].ContentJson)
            .First(x => x.Kind == NovelContentBlock.ImageKind);
        Assert.IsTrue(NovelVolumeAssetStore.IsAssetName(image.Source), image.Source);
        Assert.IsNotNull(fixture.Assets.Resolve(volume.Id, image.Source));
        Assert.IsNotNull(volume.CoverAsset);
        Assert.IsNotNull(fixture.Assets.Resolve(volume.Id, volume.CoverAsset));
        Assert.IsNull(fixture.Assets.Resolve(volume.Id, "../../etc/passwd"));
    }

    [TestMethod]
    public async Task MalformedEpubsFailPerFileWithUsefulDiagnostics()
    {
        await using var fixture = await Fixture.CreateAsync();

        using var valid = Volume(1, "正常な本文です。").Build();
        using var notZip = new MemoryStream("this is not an epub"u8.ToArray());
        using var noContainer = BuildWithoutContainer();
        var drmBuilder = Volume(2, "暗号化された本文です。");
        drmBuilder.EncryptionAlgorithm = "http://www.w3.org/2001/04/xmlenc#aes128-cbc";
        using var drm = drmBuilder.Build();
        using var noText = new EpubTestBuilder().RawChapter("text/empty.xhtml", "<p>短い</p>").Build();

        var outcomes = await fixture.Imports.ImportUploadsAsync(
            [
                (notZip, "broken.epub"),
                (valid, "valid.epub"),
                (noContainer, "container.epub"),
                (drm, "drm.epub"),
                (noText, "empty.epub"),
                (new MemoryStream(), "notes.txt")
            ],
            targetWorkId: null,
            CancellationToken.None);

        var byFile = outcomes.ToDictionary(x => x.FileName);
        Assert.IsTrue(byFile["valid.epub"].Succeeded, byFile["valid.epub"].Message);
        StringAssert.Contains(byFile["broken.epub"].Message, "not a valid EPUB");
        StringAssert.Contains(byFile["container.epub"].Message, "META-INF/container.xml");
        StringAssert.Contains(byFile["drm.epub"].Message, "DRM");
        StringAssert.Contains(byFile["empty.epub"].Message, "readable text");
        StringAssert.Contains(byFile["notes.txt"].Message, ".epub");
        Assert.AreEqual(5, outcomes.Count(x => !x.Succeeded));

        Assert.AreEqual(1, await fixture.Db.NovelVolumes.CountAsync());
        Assert.AreEqual(1, await fixture.Db.NovelChapters.CountAsync());
        StringAssert.Contains(NovelEpubImportOutcome.Summarize(outcomes), "1 of 6 EPUB volumes imported.");
    }

    [TestMethod]
    public async Task InboxImportGroupsSubfoldersAndNeverModifiesSourceFiles()
    {
        await using var fixture = await Fixture.CreateAsync();
        var inbox = Path.Combine(fixture.Root, "inbox");
        var seriesFolder = Path.Combine(inbox, NovelEpubImportService.InboxFolder, "Folder Series");
        Directory.CreateDirectory(seriesFolder);

        var one = Path.Combine(seriesFolder, "one.epub");
        var two = Path.Combine(seriesFolder, "two.epub");
        var bytesOne = new EpubTestBuilder { Title = "Other 1", Identifier = "urn:x:1" }
            .Chapter("text/a.xhtml", "一", "フォルダの一巻です。").BuildBytes();
        var bytesTwo = new EpubTestBuilder { Title = "Other 2", Identifier = "urn:x:2" }
            .Chapter("text/a.xhtml", "二", "フォルダの二巻です。").BuildBytes();
        await File.WriteAllBytesAsync(one, bytesOne);
        await File.WriteAllBytesAsync(two, bytesTwo);
        File.SetAttributes(one, FileAttributes.ReadOnly);
        var writeTime = File.GetLastWriteTimeUtc(one);

        var first = await fixture.Imports.ImportInboxAsync(inbox, CancellationToken.None);
        var second = await fixture.Imports.ImportInboxAsync(inbox, CancellationToken.None);

        Assert.IsTrue(first.All(x => x.Succeeded), NovelEpubImportOutcome.Summarize(first));
        Assert.IsTrue(second.All(x => x.Message.Contains("already up to date", StringComparison.Ordinal)));
        var work = await fixture.Db.NovelWorks.SingleAsync();
        Assert.AreEqual("Folder Series", work.Title);
        CollectionAssert.AreEqual(
            new[] { 1, 2 },
            await fixture.Db.NovelVolumes.OrderBy(x => x.Number).Select(x => x.Number).ToArrayAsync());
        CollectionAssert.AreEqual(bytesOne, await File.ReadAllBytesAsync(one));
        Assert.AreEqual(writeTime, File.GetLastWriteTimeUtc(one));
        File.SetAttributes(one, FileAttributes.Normal);
    }

    [TestMethod]
    public void VolumeNumbersAndSeriesNamesAreReadFromTitles()
    {
        Assert.AreEqual(3, NovelEpubImportService.ParseVolumeNumber("魔法の国 第三巻"));
        Assert.AreEqual(12, NovelEpubImportService.ParseVolumeNumber("魔法の国 第十二巻"));
        Assert.AreEqual(4, NovelEpubImportService.ParseVolumeNumber("魔法の国（４）"));
        Assert.AreEqual(2, NovelEpubImportService.ParseVolumeNumber("Magic Land Vol. 2: The Return"));
        Assert.AreEqual(5, NovelEpubImportService.ParseVolumeNumber("Magic Land 5"));
        Assert.IsNull(NovelEpubImportService.ParseVolumeNumber("Magic Land"));
        Assert.AreEqual("Magic Land", NovelEpubImportService.StripVolumeMarker("Magic Land Vol. 2: The Return"));
        Assert.AreEqual("魔法の国", NovelEpubImportService.StripVolumeMarker("魔法の国 第三巻"));
        Assert.AreEqual(
            NovelEpubImportService.SeriesKey("Magic Land"),
            NovelEpubImportService.SeriesKey("magic  land:"));
    }

    [TestMethod]
    public async Task EpubVolumeProgressMapsToAniListVolumesThroughExistingSegments()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ImportAsync(Volume(1, "一巻一章です。", "一巻二章です。"));
        await fixture.ImportAsync(Volume(2, "二巻一章です。", "二巻二章です。"));
        var work = await fixture.Db.NovelWorks.SingleAsync();
        work.MetadataProvider = "anilist";
        work.MetadataExternalId = "321";
        await fixture.Db.SaveChangesAsync();
        var chapters = await fixture.Db.NovelChapters.AsNoTracking().OrderBy(x => x.Number).ToListAsync();

        await fixture.Segments.AddAsync(new ReadingMediaSegmentMapping(
            Guid.Empty,
            "novel",
            work.Id.ToString(),
            1,
            4,
            1,
            "anilist",
            "321",
            "Series",
            null,
            LocalVolumeStart: 1,
            LocalVolumeEnd: 2,
            RemoteVolumeStart: 1,
            DateTimeOffset.UtcNow));

        await new NovelProgressService(fixture.Db).SaveProgressAsync(
            "owner", chapters[2].Id, 1000, "ja", 0, 0, CancellationToken.None);
        await fixture.ConnectAniListAsync("owner");

        var preview = await fixture.AniList("owner", AniListExternalProgressTests.FakeAniList.OnList(progress: 1, chapters: 10))
            .GetNovelProgressPreviewAsync(work.Id, CancellationToken.None);

        Assert.IsTrue(preview.CanSync, preview.Message);
        Assert.AreEqual(3, preview.RequestedProgress);
        Assert.AreEqual(2, preview.RequestedVolumeProgress);
    }

    [TestMethod]
    public async Task MigrationMovesExistingWorksIntoOneImplicitVolumeWithoutLosingNotes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"anilingo-volumes-{Guid.NewGuid():N}.db");
        try
        {
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={path};Foreign Keys=True")
                .Options);
            await db.GetService<IMigrator>().MigrateAsync(DatabaseMigrationBridge.RetireLegacyLearningStateMigration);

            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO NovelWorks (Id, SourceProvider, SourceKey, SourceUrl, Title, ImportedAt, UpdatedAt) VALUES
                    ('AAAAAAAA-0000-0000-0000-000000000001', 'ncode', 'n1', 'https://ncode.syosetu.com/n1/', 'Web', '2026-01-01 00:00:00', '2026-01-01 00:00:00'),
                    ('AAAAAAAA-0000-0000-0000-000000000002', 'book-epub', 'b1', 'upload://b.epub', 'Book', '2026-01-01 00:00:00', '2026-01-01 00:00:00');
                INSERT INTO NovelChapters (Id, WorkId, Number, SourceUrl, Title, OriginalText, SourceHash, ImportedAt, UpdatedAt) VALUES
                    ('CCCCCCCC-0000-0000-0000-000000000001', 'AAAAAAAA-0000-0000-0000-000000000001', 1, 'https://ncode.syosetu.com/n1/1/', 'One', '本文。', 'H1', '2026-01-01 00:00:00', '2026-01-01 00:00:00'),
                    ('CCCCCCCC-0000-0000-0000-000000000002', 'AAAAAAAA-0000-0000-0000-000000000001', 2, 'https://ncode.syosetu.com/n1/2/', 'Two', '', '', '2026-01-01 00:00:00', '2026-01-01 00:00:00'),
                    ('CCCCCCCC-0000-0000-0000-000000000003', 'AAAAAAAA-0000-0000-0000-000000000002', 1, 'book://b/1', 'Chapter', 'Text.', 'H3', '2026-01-01 00:00:00', '2026-01-01 00:00:00');
                INSERT INTO NovelTranslations (Id, ChapterId, TargetLanguage, ProviderId, PromptVersion, SourceHash, Text, CreatedAt) VALUES
                    ('DDDDDDDD-0000-0000-0000-000000000001', 'CCCCCCCC-0000-0000-0000-000000000001', 'de', 'p', 1, 'H1', 'Text.', '2026-01-01 00:00:00');
                INSERT INTO NovelProgress (Id, ProfileId, WorkId, ChapterId, PositionPermille, AnchorLanguage, AnchorOffset, UpdatedAt) VALUES
                    ('EEEEEEEE-0000-0000-0000-000000000001', 'reader', 'AAAAAAAA-0000-0000-0000-000000000001', 'CCCCCCCC-0000-0000-0000-000000000001', 400, 'ja', 0, '2026-01-01 00:00:00');
                INSERT INTO NovelBookmarks (Id, ProfileId, WorkId, ChapterId, PositionPermille, Language, CharacterOffset, CreatedAt) VALUES
                    ('FFFFFFFF-0000-0000-0000-000000000001', 'reader', 'AAAAAAAA-0000-0000-0000-000000000001', 'CCCCCCCC-0000-0000-0000-000000000001', 400, 'ja', 0, '2026-01-01 00:00:00');
                INSERT INTO NovelHighlights (Id, ProfileId, WorkId, ChapterId, Language, ParagraphIndex, StartOffset, EndOffset, Text, CreatedAt) VALUES
                    ('BBBBBBBB-0000-0000-0000-000000000001', 'reader', 'AAAAAAAA-0000-0000-0000-000000000001', 'CCCCCCCC-0000-0000-0000-000000000001', 'ja', 0, 0, 2, '本文', '2026-01-01 00:00:00');
                """);

            await DatabaseMigrationBridge.UpgradeAsync(db);

            var volumes = await db.NovelVolumes.AsNoTracking().OrderBy(x => x.Kind).ToListAsync();
            Assert.AreEqual(2, volumes.Count);
            Assert.AreEqual(NovelVolumeKinds.Book, volumes[0].Kind);
            Assert.AreEqual(NovelVolumeKinds.Web, volumes[1].Kind);
            Assert.IsTrue(volumes.All(x => x.Number == 1));

            var chapters = await db.NovelChapters.AsNoTracking().ToListAsync();
            Assert.AreEqual(3, chapters.Count);
            Assert.IsTrue(chapters.All(chapter =>
                volumes.Single(volume => volume.Id == chapter.VolumeId).WorkId == chapter.WorkId));

            Assert.AreEqual(1, await db.NovelTranslations.CountAsync());
            Assert.AreEqual(1, await db.NovelProgress.CountAsync());
            Assert.AreEqual(1, await db.NovelBookmarks.CountAsync());
            Assert.AreEqual(1, await db.NovelHighlights.CountAsync());

            var connection = db.Database.GetDbConnection();
            await connection.OpenAsync();
            await using (var check = connection.CreateCommand())
            {
                check.CommandText = "PRAGMA foreign_key_check;";
                await using var reader = await check.ExecuteReaderAsync();
                Assert.IsFalse(await reader.ReadAsync(), "Foreign keys must be consistent after the rebuild.");
            }

            await using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA foreign_keys;";
                Assert.AreEqual(1L, await pragma.ExecuteScalarAsync());
            }

            await connection.CloseAsync();
            Assert.IsFalse(db.Database.HasPendingModelChanges());

            // Deleting a volume cascades to its chapters only.
            await db.NovelVolumes.Where(x => x.Kind == NovelVolumeKinds.Book).ExecuteDeleteAsync();
            Assert.AreEqual(2, await db.NovelChapters.CountAsync());
            Assert.AreEqual(1, await db.NovelProgress.CountAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task NarouImportAndBooksImportUseTheImplicitVolume()
    {
        await using var fixture = await Fixture.CreateAsync();
        var imports = new NovelImportService(fixture.Db, [new FakeWebSource()]);
        var workId = await imports.ImportWorkAsync("https://example.invalid/web", CancellationToken.None);
        await imports.ImportWorkAsync("https://example.invalid/web", CancellationToken.None);

        var volume = await fixture.Db.NovelVolumes.SingleAsync(x => x.WorkId == workId);
        Assert.AreEqual(NovelVolumeKinds.Web, volume.Kind);
        Assert.IsTrue(await fixture.Db.NovelChapters.AllAsync(x => x.VolumeId == volume.Id));

        var detail = (await fixture.Catalog.GetWorkDetailAsync(workId, CancellationToken.None))!;
        Assert.IsFalse(detail.IsEpubSeries);
        Assert.AreEqual(1, detail.Volumes.Count);
        Assert.AreEqual(2, detail.Chapters.Count);
    }

    /// <summary>A volume of <see cref="Series"/> with a cover and one chapter per text.</summary>
    private static EpubTestBuilder Volume(int number, params string[] chapterTexts)
    {
        var builder = new EpubTestBuilder
        {
            Title = $"{Series} {number}",
            Identifier = $"urn:uuid:series-volume-{number}",
            CalibreSeries = Series,
            CalibreSeriesIndex = number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            CoverPath = "images/cover.png"
        };

        for (var index = 0; index < chapterTexts.Length; index++)
        {
            builder.Chapter($"text/ch{index + 1}.xhtml", chapterTexts[index][..3], chapterTexts[index]);
        }

        return builder.Image("images/cover.png");
    }

    private static MemoryStream BuildWithoutContainer()
    {
        var stream = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(
                   stream,
                   System.IO.Compression.ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("mimetype").Open());
            writer.Write("application/epub+zip");
        }

        stream.Position = 0;
        return stream;
    }

    private sealed class FakeWebSource : INovelSourceProvider
    {
        public string Key => "fake-web";

        public bool CanHandle(Uri sourceUri) => sourceUri.Host == "example.invalid";

        public Task<NovelSourceWorkSnapshot> GetWorkAsync(Uri sourceUri, CancellationToken cancellationToken) =>
            Task.FromResult(new NovelSourceWorkSnapshot(
                Key,
                "web",
                "https://example.invalid/web",
                "Web Novel",
                null,
                null,
                [
                    new NovelSourceChapterReference(1, "One", "https://example.invalid/web/1"),
                    new NovelSourceChapterReference(2, "Two", "https://example.invalid/web/2")
                ]));

        public Task<NovelSourceChapterSnapshot> GetChapterAsync(Uri sourceUri, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not used.");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly AniListAccountStore accountStore;
        private readonly MediaMappingReviewStore reviewStore;

        private Fixture(string root, AppDbContext db)
        {
            Root = root;
            Db = db;
            Assets = new NovelVolumeAssetStore(new DirectoryInfo(Path.Combine(root, "volumes")));
            Imports = new NovelEpubImportService(db, Assets, NullLogger<NovelEpubImportService>.Instance);
            Catalog = new NovelCatalogQueries(db);
            var mappings = new DirectoryInfo(Path.Combine(root, "anilist"));
            Segments = new ReadingSegmentMappingStore(NullLogger<ReadingSegmentMappingStore>.Instance, mappings);
            reviewStore = new MediaMappingReviewStore(NullLogger<MediaMappingReviewStore>.Instance, mappings);
            accountStore = new AniListAccountStore(
                new EphemeralDataProtectionProvider(),
                NullLogger<AniListAccountStore>.Instance,
                new DirectoryInfo(root));
        }

        public string Root { get; }
        public AppDbContext Db { get; }
        public NovelVolumeAssetStore Assets { get; }
        public NovelEpubImportService Imports { get; }
        public NovelCatalogQueries Catalog { get; }
        public ReadingSegmentMappingStore Segments { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "anilingo-tests", $"novel-volumes-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(root, "anilist"));
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={Path.Combine(root, "anilingo.db")};Foreign Keys=True")
                .Options);
            await DatabaseMigrationBridge.UpgradeAsync(db);
            return new Fixture(root, db);
        }

        public async Task<NovelEpubImportOutcome> ImportAsync(EpubTestBuilder builder)
        {
            using var stream = builder.Build();
            var outcomes = await Imports.ImportUploadsAsync(
                [(stream, $"{builder.Title}.epub")],
                targetWorkId: null,
                CancellationToken.None);
            return outcomes.Single();
        }

        public Task ConnectAniListAsync(string profileId) =>
            accountStore.SaveAsync(
                profileId,
                new StoredAniListAccount(12345, 42, "viewer", null, "token", DateTimeOffset.UtcNow, null),
                CancellationToken.None);

        public AniListAccountService AniList(string profileId, HttpMessageHandler remote) =>
            new(
                new HttpClient(remote) { BaseAddress = new Uri("https://graphql.anilist.co/") },
                accountStore,
                Db,
                new AnimeMetadataService(Db, [], accountStore, reviewStore),
                Segments,
                reviewStore,
                EpisodeFlowFixture.Account(profileId),
                NullLogger<AniListAccountService>.Instance);

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
