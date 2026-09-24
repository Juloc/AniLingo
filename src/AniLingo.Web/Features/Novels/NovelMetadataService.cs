using System.Text.Json;
using AniLingo.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Novels;

public sealed class NovelMetadataService(
    AppDbContext db,
    IEnumerable<INovelMetadataProvider> providers)
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

    private INovelMetadataProvider GetProvider(string providerKey) =>
        providers.FirstOrDefault(
            provider => provider.Key.Equals(providerKey, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException(
            $"Novel metadata provider '{providerKey}' is not registered.");
}
