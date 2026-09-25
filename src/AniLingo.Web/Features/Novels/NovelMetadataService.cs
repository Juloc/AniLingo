using System.Text.Json;
using AniLingo.Web.Data;
using AniLingo.Web.Features.MediaMapping;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Novels;

public sealed class NovelMetadataService(
    AppDbContext db,
    IEnumerable<INovelMetadataProvider> providers,
    MediaMappingReviewStore reviewStore)
{
    public async Task<IReadOnlyList<NovelMetadataCandidate>> SearchAsync(
        string providerKey,
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var provider = GetProvider(providerKey);
        return await provider.SearchAsync(query, limit, cancellationToken);
    }

    public async Task<AutomaticMediaMatchDecision> AutoMatchAsync(
        Guid workId,
        CancellationToken cancellationToken)
    {
        var work = await db.NovelWorks
            .AsNoTracking()
            .Where(x => x.Id == workId)
            .Select(x => new
            {
                x.Title,
                x.MetadataProvider,
                x.MetadataExternalId,
                ChapterCount = db.NovelChapters.Count(chapter => chapter.WorkId == x.Id)
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (work is null)
        {
            return new AutomaticMediaMatchDecision(
                AutomaticMediaMatchDisposition.None,
                null,
                0,
                0,
                ["Novel work was not found."]);
        }

        if (!string.IsNullOrWhiteSpace(work.MetadataProvider) &&
            !string.IsNullOrWhiteSpace(work.MetadataExternalId))
        {
            return new AutomaticMediaMatchDecision(
                AutomaticMediaMatchDisposition.None,
                null,
                0,
                0,
                ["Novel already has an explicit metadata match."]);
        }

        var provider = GetProvider(NovelAniListProvider.ProviderKey);
        IReadOnlyList<NovelMetadataCandidate> candidates;
        try
        {
            candidates = await provider.SearchAsync(
                work.Title,
                8,
                cancellationToken);
        }
        catch (NovelMetadataProviderException)
        {
            return new AutomaticMediaMatchDecision(
                AutomaticMediaMatchDisposition.None,
                null,
                0,
                0,
                ["AniList metadata is currently unavailable."]);
        }

        var decision = AutomaticMediaMatcher.Select(
            new AutomaticMediaMatchInput(
                work.Title,
                UnitCount: work.ChapterCount > 0 ? work.ChapterCount : null,
                Format: "LIGHT_NOVEL"),
            candidates.Select(candidate => new AutomaticMediaMatchCandidate(
                candidate.Provider,
                candidate.ExternalId,
                candidate.PreferredTitle,
                new[]
                {
                    candidate.PreferredTitle,
                    candidate.NativeTitle ?? ""
                },
                UnitCount: candidate.ChapterCount,
                Format: candidate.Format)));

        if (decision.CanApply && decision.Candidate is not null)
        {
            try
            {
                await MatchAsync(
                    workId,
                    decision.Candidate.Provider,
                    decision.Candidate.ExternalId,
                    cancellationToken);

                await reviewStore.ResolveAsync(
                    "novel",
                    workId.ToString(),
                    "identity",
                    cancellationToken);
            }
            catch (InvalidOperationException exception)
            {
                var reviewDecision = decision with
                {
                    Disposition = AutomaticMediaMatchDisposition.Review,
                    Evidence = decision.Evidence
                        .Append(exception.Message)
                        .ToArray()
                };

                await SaveIdentityReviewAsync(
                    workId,
                    work.Title,
                    reviewDecision,
                    cancellationToken);
                return reviewDecision;
            }
        }
        else if (decision.Candidate is not null)
        {
            await SaveIdentityReviewAsync(
                workId,
                work.Title,
                decision,
                cancellationToken);
        }

        return decision;
    }

    public async Task MatchAsync(
        Guid workId,
        string providerKey,
        string externalId,
        CancellationToken cancellationToken)
    {
        var work = await db.NovelWorks
            .SingleOrDefaultAsync(x => x.Id == workId, cancellationToken)
            ?? throw new InvalidOperationException("Novel work was not found.");

        var provider = GetProvider(providerKey);
        var candidate = await provider.GetAsync(externalId, cancellationToken)
            ?? throw new InvalidOperationException("The AniList novel was not found.");

        var duplicate = await db.NovelWorks
            .AsNoTracking()
            .AnyAsync(
                x => x.Id != workId &&
                    x.MetadataProvider == provider.Key &&
                    x.MetadataExternalId == candidate.ExternalId,
                cancellationToken);

        if (duplicate)
        {
            throw new InvalidOperationException(
                "This AniList novel is already matched to another imported work.");
        }

        work.MetadataProvider = candidate.Provider;
        work.MetadataExternalId = candidate.ExternalId;
        work.MetadataTitle = candidate.PreferredTitle;
        work.MetadataNativeTitle = candidate.NativeTitle;
        work.MetadataDescription = candidate.Description;
        work.CoverImageUrl = candidate.CoverImageUrl;
        work.BannerImageUrl = candidate.BannerImageUrl;
        work.Format = candidate.Format;
        work.MetadataStatus = candidate.Status;
        work.MetadataChapterCount = candidate.ChapterCount;
        work.MetadataVolumeCount = candidate.VolumeCount;
        work.MetadataGenresJson = candidate.Genres is { Count: > 0 }
            ? JsonSerializer.Serialize(candidate.Genres)
            : null;
        work.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        await reviewStore.ResolveAsync(
            "novel",
            workId.ToString(),
            "identity",
            cancellationToken);
    }

    public async Task RemoveAsync(
        Guid workId,
        CancellationToken cancellationToken)
    {
        var work = await db.NovelWorks
            .SingleOrDefaultAsync(x => x.Id == workId, cancellationToken);

        if (work is null)
        {
            return;
        }

        work.MetadataProvider = null;
        work.MetadataExternalId = null;
        work.MetadataTitle = null;
        work.MetadataNativeTitle = null;
        work.MetadataDescription = null;
        work.CoverImageUrl = null;
        work.BannerImageUrl = null;
        work.Format = null;
        work.MetadataStatus = null;
        work.MetadataChapterCount = null;
        work.MetadataVolumeCount = null;
        work.MetadataGenresJson = null;
        work.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SaveIdentityReviewAsync(
        Guid workId,
        string localTitle,
        AutomaticMediaMatchDecision decision,
        CancellationToken cancellationToken)
    {
        if (decision.Candidate is null)
        {
            return;
        }

        var reason = decision.Disposition == AutomaticMediaMatchDisposition.Review
            ? $"AniList identity needs review: score {decision.Score}, runner-up {decision.RunnerUpScore}."
            : $"AniList identity confidence is too low for automatic matching: score {decision.Score}.";

        await reviewStore.UpsertAsync(
            "novel",
            workId.ToString(),
            localTitle,
            "identity",
            reason,
            [
                new MediaMappingReviewCandidate(
                    decision.Candidate.Provider,
                    decision.Candidate.ExternalId,
                    decision.Candidate.PreferredTitle,
                    decision.Score,
                    decision.Evidence,
                    decision.Candidate.Format,
                    decision.Candidate.Year,
                    decision.Candidate.UnitCount)
            ],
            cancellationToken);
    }

    private INovelMetadataProvider GetProvider(string providerKey) =>
        providers.FirstOrDefault(
            provider => provider.Key.Equals(providerKey, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException(
            $"Novel metadata provider '{providerKey}' is not registered.");
}
