using AniLingo.Web.Features.Operations;
using AniLingo.Web.Infrastructure;

namespace AniLingo.Web.Features.Novels;

/// <summary>
/// Novel background work queued through the canonical Operations queue:
/// chapter downloads, AI chapter translation and AI episode mapping.
/// </summary>
public sealed class NovelJobs(BackgroundJobQueue jobs)
{
    public const string ChapterDownloadKind = "novel-chapter-download";
    public const string ChapterTranslationKind = "novel-chapter-translation";
    public const string EpisodeMappingKind = "novel-episode-mapping";

    public async Task<Guid> QueueChapterDownloadAsync(
        string subject,
        IReadOnlyList<Guid> chapterIds,
        string profileId,
        CancellationToken cancellationToken)
    {
        var ids = chapterIds.ToArray();
        return await jobs.QueueAsync(
            new OperationDescriptor(
                ChapterDownloadKind,
                "Novels",
                ids.Length == 1 ? "Download novel chapter" : "Download novel chapters",
                subject,
                profileId,
                OperationLane.Normal,
                IsDownload: true,
                Retryable: true),
            async (operation, services, workerToken) =>
            {
                var imports = services.GetRequiredService<NovelImportService>();

                for (var index = 0; index < ids.Length; index++)
                {
                    await operation.ReportAsync(
                        Math.Clamp((int)Math.Round(index * 100d / ids.Length), 0, 99),
                        $"Downloading chapter {index + 1} of {ids.Length}.",
                        cancellationToken: workerToken);

                    await imports.DownloadChapterContentAsync(
                        ids[index],
                        forceRefresh: false,
                        workerToken);

                    if (index < ids.Length - 1)
                    {
                        await Task.Delay(150, workerToken);
                    }
                }

                await operation.ReportAsync(
                    100,
                    $"Downloaded {ids.Length} chapter(s).",
                    cancellationToken: workerToken);
            },
            cancellationToken);
    }

    public async Task<Guid> QueueTranslationAsync(
        Guid chapterId,
        string subject,
        string profileId,
        CancellationToken cancellationToken) =>
        await jobs.QueueAsync(
            new OperationDescriptor(
                ChapterTranslationKind,
                "Translation",
                "Translate novel chapter",
                subject,
                profileId,
                OperationLane.Normal,
                Retryable: true),
            async (operation, services, workerToken) =>
            {
                await operation.ReportAsync(
                    5,
                    "Translating chapter to German.",
                    cancellationToken: workerToken);

                var service = services.GetRequiredService<NovelTranslationService>();
                await service.TranslateChapterAsync(
                    chapterId,
                    NovelReadingLanguage.German,
                    workerToken);

                await operation.ReportAsync(
                    100,
                    "German chapter translation completed.",
                    cancellationToken: workerToken);
            },
            cancellationToken);

    public async Task<Guid> QueueEpisodeMappingAsync(
        Guid workId,
        Guid animeId,
        string subject,
        string profileId,
        CancellationToken cancellationToken) =>
        await jobs.QueueAsync(
            new OperationDescriptor(
                EpisodeMappingKind,
                "AI",
                "Suggest novel episode mappings",
                subject,
                profileId,
                OperationLane.Normal,
                Retryable: true),
            async (operation, services, workerToken) =>
            {
                await operation.ReportAsync(
                    5,
                    "Generating mapping suggestions.",
                    cancellationToken: workerToken);

                var service = services.GetRequiredService<NovelMappingService>();
                await service.SuggestAsync(workId, animeId, workerToken);

                await operation.ReportAsync(
                    100,
                    "Mapping suggestions are ready.",
                    cancellationToken: workerToken);
            },
            cancellationToken);
}
