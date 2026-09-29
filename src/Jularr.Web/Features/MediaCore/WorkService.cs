using Jularr.Web.Data;
using Jularr.Web.Features.Mapping;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.MediaCore;

/// <summary>
/// The write surface of the universal media core (#592). Creates works, attaches normalized external
/// identities, titles, structure (seasons/episodes, volumes/chapters), editions/versions, typed
/// relations and field-level provenance — all provider-independent and idempotent. Provider mappings
/// are <b>correctable</b>: <see cref="ReassignExternalIdentityAsync"/> moves a provider id to a
/// different work and records the correction (reusing the #525 anime mapping audit where the work
/// bridges to an anime). No operation ever matches on a filename alone (#556).
/// </summary>
public sealed class WorkService(AppDbContext db)
{
    /// <summary>Creates a new work with a stable id and a cached canonical title.</summary>
    public async Task<Work> CreateWorkAsync(
        WorkMediaType mediaType,
        string canonicalTitle,
        int? year,
        CancellationToken cancellationToken)
    {
        var work = new Work
        {
            MediaType = mediaType,
            CanonicalTitle = canonicalTitle.Trim(),
            Year = year
        };
        db.Set<Work>().Add(work);
        await db.SaveChangesAsync(cancellationToken);
        return work;
    }

    /// <summary>
    /// Resolves the work a provider identity already points at, or creates a new work and links the
    /// identity. Idempotent: the same (media type, provider, external id) always returns the same work.
    /// </summary>
    public async Task<Work> EnsureWorkByExternalIdentityAsync(
        WorkMediaType mediaType,
        string provider,
        string externalId,
        string title,
        int? year,
        CancellationToken cancellationToken)
    {
        var normalizedProvider = MediaCoreNormalization.NormalizeProvider(provider);
        var normalizedExternalId = MediaCoreNormalization.NormalizeExternalId(externalId);
        if (normalizedProvider.Length == 0 || normalizedExternalId.Length == 0)
        {
            throw new ArgumentException("A provider and external id are required to resolve a work.");
        }

        var existing = await db.Set<WorkExternalIdentity>()
            .Where(x => x.MediaType == mediaType
                && x.Provider == normalizedProvider
                && x.ExternalId == normalizedExternalId)
            .Select(x => x.WorkId)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing != Guid.Empty)
        {
            return await db.Set<Work>().FirstAsync(x => x.Id == existing, cancellationToken);
        }

        var work = await CreateWorkAsync(mediaType, title, year, cancellationToken);
        await LinkExternalIdentityAsync(
            work.Id, mediaType, normalizedProvider, normalizedExternalId,
            confidence: 1.0, evidence: "provider id", isPrimary: true,
            isManualOverride: false, MappingReviewState.Confirmed, cancellationToken);
        return work;
    }

    /// <summary>
    /// Upserts an external identity on a work. If the identity is already claimed by <b>another</b> work
    /// the mapping is left untouched (use <see cref="ReassignExternalIdentityAsync"/> to correct it),
    /// and this returns <c>false</c>. A manual identity is never downgraded by a non-manual upsert.
    /// </summary>
    public async Task<bool> LinkExternalIdentityAsync(
        Guid workId,
        WorkMediaType mediaType,
        string provider,
        string externalId,
        double confidence,
        string evidence,
        bool isPrimary,
        bool isManualOverride,
        MappingReviewState reviewState,
        CancellationToken cancellationToken)
    {
        var normalizedProvider = MediaCoreNormalization.NormalizeProvider(provider);
        var normalizedExternalId = MediaCoreNormalization.NormalizeExternalId(externalId);

        var existing = await db.Set<WorkExternalIdentity>()
            .FirstOrDefaultAsync(
                x => x.MediaType == mediaType
                    && x.Provider == normalizedProvider
                    && x.ExternalId == normalizedExternalId,
                cancellationToken);

        if (existing is not null)
        {
            if (existing.WorkId != workId)
            {
                return false; // claimed by another work; correction is an explicit operation
            }

            if (existing.IsManualOverride && !isManualOverride)
            {
                return false; // never downgrade an owner correction on refresh
            }

            existing.Confidence = confidence;
            existing.Evidence = evidence.Trim();
            existing.IsPrimary = isPrimary;
            existing.IsManualOverride = isManualOverride || existing.IsManualOverride;
            existing.ReviewState = reviewState;
            existing.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        if (isPrimary)
        {
            await ClearPrimaryIdentityAsync(workId, mediaType, normalizedProvider, cancellationToken);
        }

        db.Set<WorkExternalIdentity>().Add(new WorkExternalIdentity
        {
            WorkId = workId,
            MediaType = mediaType,
            Provider = normalizedProvider,
            ExternalId = normalizedExternalId,
            Confidence = confidence,
            Evidence = evidence.Trim(),
            IsPrimary = isPrimary,
            IsManualOverride = isManualOverride,
            ReviewState = reviewState
        });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Correctable provider mapping (#592, reuses #525 intent): moves a provider identity to
    /// <paramref name="targetWorkId"/>, marking it a confirmed manual override so later provider
    /// refreshes cannot undo it. When the target work bridges to an anime, the correction is appended
    /// to the existing anime mapping audit trail. Returns the affected identity, or null if unknown.
    /// </summary>
    public async Task<WorkExternalIdentity?> ReassignExternalIdentityAsync(
        WorkMediaType mediaType,
        string provider,
        string externalId,
        Guid targetWorkId,
        string actor,
        string evidence,
        CancellationToken cancellationToken)
    {
        var normalizedProvider = MediaCoreNormalization.NormalizeProvider(provider);
        var normalizedExternalId = MediaCoreNormalization.NormalizeExternalId(externalId);

        var identity = await db.Set<WorkExternalIdentity>()
            .FirstOrDefaultAsync(
                x => x.MediaType == mediaType
                    && x.Provider == normalizedProvider
                    && x.ExternalId == normalizedExternalId,
                cancellationToken);

        if (identity is null)
        {
            return null;
        }

        var previousWorkId = identity.WorkId;
        identity.WorkId = targetWorkId;
        identity.IsManualOverride = true;
        identity.ReviewState = MappingReviewState.Confirmed;
        identity.Evidence = string.IsNullOrWhiteSpace(evidence) ? "owner correction" : evidence.Trim();
        identity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await TryAppendAnimeMappingAuditAsync(
            targetWorkId,
            action: MappingAuditStore.ActionApply,
            summary: $"Reassigned {normalizedProvider}:{normalizedExternalId}",
            details: $"from work {previousWorkId:D} to work {targetWorkId:D}; {identity.Evidence}",
            actor: actor,
            cancellationToken);

        return identity;
    }

    /// <summary>Sets the review state of an external identity (confirm / flag / reject).</summary>
    public async Task<bool> SetExternalIdentityReviewStateAsync(
        Guid identityId,
        MappingReviewState reviewState,
        CancellationToken cancellationToken)
    {
        var identity = await db.Set<WorkExternalIdentity>()
            .FirstOrDefaultAsync(x => x.Id == identityId, cancellationToken);
        if (identity is null)
        {
            return false;
        }

        identity.ReviewState = reviewState;
        identity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Adds or refreshes a title, de-duplicated by (work, type, language, normalized value).</summary>
    public async Task<WorkTitle> AddOrUpdateTitleAsync(
        Guid workId,
        WorkTitleType titleType,
        string language,
        string value,
        string source,
        bool isPrimary,
        CancellationToken cancellationToken)
    {
        var normalizedLanguage = (language ?? "und").Trim().ToLowerInvariant();
        var normalizedValue = MediaCoreNormalization.NormalizeTitle(value);

        var existing = await db.Set<WorkTitle>()
            .FirstOrDefaultAsync(
                x => x.WorkId == workId
                    && x.TitleType == titleType
                    && x.Language == normalizedLanguage
                    && x.NormalizedValue == normalizedValue,
                cancellationToken);

        if (isPrimary)
        {
            await ClearPrimaryTitleAsync(workId, cancellationToken);
        }

        if (existing is not null)
        {
            existing.Value = value.Trim();
            existing.Source = MediaCoreNormalization.NormalizeProvider(source);
            existing.IsPrimary = isPrimary || existing.IsPrimary;
            await PersistPrimaryTitleCacheAsync(workId, existing, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return existing;
        }

        var title = new WorkTitle
        {
            WorkId = workId,
            TitleType = titleType,
            Language = normalizedLanguage,
            Value = value.Trim(),
            NormalizedValue = normalizedValue,
            Source = MediaCoreNormalization.NormalizeProvider(source),
            IsPrimary = isPrimary
        };
        db.Set<WorkTitle>().Add(title);
        await PersistPrimaryTitleCacheAsync(workId, title, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return title;
    }

    /// <summary>Upserts a typed relation edge, unique per (from, to, type). Optionally writes the inverse edge too.</summary>
    public async Task AddRelationAsync(
        Guid fromWorkId,
        Guid toWorkId,
        WorkRelationType relationType,
        string source,
        bool isManualOverride,
        bool includeInverse,
        CancellationToken cancellationToken)
    {
        if (fromWorkId == toWorkId)
        {
            throw new ArgumentException("A work cannot relate to itself.");
        }

        await UpsertRelationAsync(fromWorkId, toWorkId, relationType, source, isManualOverride, cancellationToken);
        if (includeInverse)
        {
            await UpsertRelationAsync(
                toWorkId, fromWorkId, WorkRelationTypes.Inverse(relationType), source, isManualOverride, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Records where a field's value came from, applying the #435 precedence ladder. Returns true when
    /// the incoming source won (a manual override is never overwritten by a provider refresh).
    /// </summary>
    public async Task<bool> SetFieldProvenanceAsync(
        Guid workId,
        string fieldKey,
        string source,
        string? providerExternalId,
        double? confidence,
        bool isManualOverride,
        string? preferredProvider,
        CancellationToken cancellationToken)
    {
        var normalizedField = (fieldKey ?? "").Trim();
        var normalizedSource = MetadataFieldSources.Normalize(source);
        var existing = await db.Set<WorkFieldProvenance>()
            .FirstOrDefaultAsync(x => x.WorkId == workId && x.FieldKey == normalizedField, cancellationToken);

        if (!MetadataFieldSources.ShouldReplace(existing, normalizedSource, isManualOverride, preferredProvider))
        {
            return false;
        }

        var priority = MetadataFieldSources.PriorityFor(normalizedSource, isManualOverride, preferredProvider);
        var now = DateTime.UtcNow;

        if (existing is null)
        {
            db.Set<WorkFieldProvenance>().Add(new WorkFieldProvenance
            {
                WorkId = workId,
                FieldKey = normalizedField,
                Source = normalizedSource,
                ProviderExternalId = providerExternalId?.Trim(),
                Confidence = confidence,
                IsManualOverride = isManualOverride,
                FallbackPriority = priority,
                FetchedAt = now,
                UpdatedAt = now
            });
        }
        else
        {
            existing.Source = normalizedSource;
            existing.ProviderExternalId = providerExternalId?.Trim();
            existing.Confidence = confidence;
            existing.IsManualOverride = isManualOverride;
            existing.FallbackPriority = priority;
            existing.FetchedAt = now;
            existing.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Bridges a work to an existing per-type record (adapter). Each legacy record maps to one work.</summary>
    public async Task<WorkSourceLink> LinkSourceAsync(
        Guid workId,
        WorkSourceKind sourceKind,
        Guid sourceId,
        CancellationToken cancellationToken)
    {
        var existing = await db.Set<WorkSourceLink>()
            .FirstOrDefaultAsync(x => x.SourceKind == sourceKind && x.SourceId == sourceId, cancellationToken);

        if (existing is not null)
        {
            if (existing.WorkId != workId)
            {
                existing.WorkId = workId;
                await db.SaveChangesAsync(cancellationToken);
            }

            return existing;
        }

        var link = new WorkSourceLink { WorkId = workId, SourceKind = sourceKind, SourceId = sourceId };
        db.Set<WorkSourceLink>().Add(link);
        await db.SaveChangesAsync(cancellationToken);
        return link;
    }

    private async Task UpsertRelationAsync(
        Guid fromWorkId,
        Guid toWorkId,
        WorkRelationType relationType,
        string source,
        bool isManualOverride,
        CancellationToken cancellationToken)
    {
        var existing = await db.Set<WorkRelation>()
            .FirstOrDefaultAsync(
                x => x.FromWorkId == fromWorkId && x.ToWorkId == toWorkId && x.RelationType == relationType,
                cancellationToken);

        if (existing is not null)
        {
            if (existing.IsManualOverride && !isManualOverride)
            {
                return; // provider refresh cannot override a manual edge
            }

            existing.Source = MediaCoreNormalization.NormalizeProvider(source);
            existing.IsManualOverride = isManualOverride || existing.IsManualOverride;
            return;
        }

        db.Set<WorkRelation>().Add(new WorkRelation
        {
            FromWorkId = fromWorkId,
            ToWorkId = toWorkId,
            RelationType = relationType,
            Source = MediaCoreNormalization.NormalizeProvider(source),
            IsManualOverride = isManualOverride
        });
    }

    private async Task ClearPrimaryIdentityAsync(
        Guid workId,
        WorkMediaType mediaType,
        string provider,
        CancellationToken cancellationToken)
    {
        var current = await db.Set<WorkExternalIdentity>()
            .Where(x => x.WorkId == workId && x.MediaType == mediaType && x.Provider == provider && x.IsPrimary)
            .ToListAsync(cancellationToken);
        foreach (var identity in current)
        {
            identity.IsPrimary = false;
        }
    }

    private async Task ClearPrimaryTitleAsync(Guid workId, CancellationToken cancellationToken)
    {
        var current = await db.Set<WorkTitle>()
            .Where(x => x.WorkId == workId && x.IsPrimary)
            .ToListAsync(cancellationToken);
        foreach (var title in current)
        {
            title.IsPrimary = false;
        }
    }

    private async Task PersistPrimaryTitleCacheAsync(Guid workId, WorkTitle title, CancellationToken cancellationToken)
    {
        if (!title.IsPrimary)
        {
            return;
        }

        var work = await db.Set<Work>().FirstOrDefaultAsync(x => x.Id == workId, cancellationToken);
        if (work is not null)
        {
            work.CanonicalTitle = title.Value;
            work.UpdatedAt = DateTime.UtcNow;
        }
    }

    private async Task TryAppendAnimeMappingAuditAsync(
        Guid workId,
        string action,
        string summary,
        string details,
        string actor,
        CancellationToken cancellationToken)
    {
        var animeSourceId = await db.Set<WorkSourceLink>()
            .Where(x => x.WorkId == workId && x.SourceKind == WorkSourceKind.Anime)
            .Select(x => (Guid?)x.SourceId)
            .FirstOrDefaultAsync(cancellationToken);

        if (animeSourceId is { } animeId)
        {
            await new MappingAuditStore(db).AppendAsync(animeId, action, summary, details, actor, cancellationToken);
        }
    }
}
