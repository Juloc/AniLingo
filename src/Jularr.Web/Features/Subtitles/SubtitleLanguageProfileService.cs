using Jularr.Web.Data;
using Jularr.Web.Features.Learning.Courses;
using Jularr.Web.Features.Watchlist;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Subtitles;

public sealed record SubtitleProfileResolution(
    SubtitleLanguageProfile Profile,
    IReadOnlyList<SubtitleLanguageProfileItem> Items);

/// <summary>
/// Owner-managed CRUD and scope resolution for <see cref="SubtitleLanguageProfile"/> (#526), the
/// Bazarr-equivalent "language profile". Follows the same shape as the existing quality/naming
/// profile stores (a default plus per-scope overrides) but backed by the relational database
/// instead of a JSON file, since profiles here reference <see cref="LibraryRoot"/> rows by id.
/// </summary>
public sealed class SubtitleLanguageProfileService(AppDbContext db)
{
    public const string DefaultProfileName = "Default";

    private static readonly SemaphoreSlim BootstrapGate = new(1, 1);

    public async Task<IReadOnlyList<SubtitleLanguageProfileDetail>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        await EnsureBootstrapAsync(cancellationToken);

        var profiles = await db.SubtitleLanguageProfiles
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var items = await db.SubtitleLanguageProfileItems
            .AsNoTracking()
            .OrderBy(x => x.SortOrder)
            .ToListAsync(cancellationToken);

        var assignments = await db.SubtitleProfileAssignments
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return profiles
            .Select(profile => new SubtitleLanguageProfileDetail(
                profile.Id,
                profile.Name,
                profile.CutoffPosition,
                items.Where(item => item.ProfileId == profile.Id).ToArray(),
                assignments.Any(a =>
                    a.ProfileId == profile.Id && a.MediaType is null && a.LibraryRootId is null),
                assignments.Count(a => a.ProfileId == profile.Id)))
            .ToArray();
    }

    public async Task<SubtitleLanguageProfileDetail?> GetDetailAsync(
        Guid profileId,
        CancellationToken cancellationToken)
    {
        var profile = await db.SubtitleLanguageProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == profileId, cancellationToken);

        if (profile is null)
        {
            return null;
        }

        var items = await db.SubtitleLanguageProfileItems
            .AsNoTracking()
            .Where(x => x.ProfileId == profileId)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(cancellationToken);

        var isGlobalDefault = await db.SubtitleProfileAssignments
            .AsNoTracking()
            .AnyAsync(a => a.ProfileId == profileId && a.MediaType == null && a.LibraryRootId == null, cancellationToken);

        var assignmentCount = await db.SubtitleProfileAssignments
            .AsNoTracking()
            .CountAsync(a => a.ProfileId == profileId, cancellationToken);

        return new SubtitleLanguageProfileDetail(
            profile.Id, profile.Name, profile.CutoffPosition, items, isGlobalDefault, assignmentCount);
    }

    /// <summary>Creates a profile when <paramref name="profileId"/> is null, otherwise replaces its items.</summary>
    public async Task<Guid> UpsertAsync(
        Guid? profileId,
        string name,
        IReadOnlyList<SubtitleLanguageProfileItemInput> items,
        int? cutoffPosition,
        CancellationToken cancellationToken)
    {
        var normalizedName = (name ?? "").Trim();
        if (normalizedName.Length == 0)
        {
            throw new InvalidDataException("Profile name is required.");
        }

        var normalizedItems = (items ?? [])
            .Select(item => new SubtitleLanguageProfileItemInput(
                (item.LanguageTag ?? "").Trim().ToLowerInvariant(),
                item.Forced,
                item.Sdh))
            .Where(item => item.LanguageTag.Length > 0)
            .ToArray();

        if (normalizedItems.Length == 0)
        {
            throw new InvalidDataException("At least one wanted language is required.");
        }

        if (cutoffPosition is int cutoff && (cutoff < 0 || cutoff >= normalizedItems.Length))
        {
            throw new InvalidDataException("Cutoff position must point at one of the wanted languages.");
        }

        SubtitleLanguageProfile profile;
        if (profileId is Guid id)
        {
            profile = await db.SubtitleLanguageProfiles.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
                ?? throw new InvalidDataException("Subtitle language profile does not exist.");

            var duplicateName = await db.SubtitleLanguageProfiles.AnyAsync(
                x => x.Id != id && x.Name == normalizedName, cancellationToken);
            if (duplicateName)
            {
                throw new InvalidDataException($"A profile named '{normalizedName}' already exists.");
            }

            await db.SubtitleLanguageProfileItems
                .Where(x => x.ProfileId == id)
                .ExecuteDeleteAsync(cancellationToken);

            profile.Name = normalizedName;
            profile.CutoffPosition = cutoffPosition;
            profile.UpdatedAtUtc = DateTime.UtcNow;
        }
        else
        {
            var duplicateName = await db.SubtitleLanguageProfiles.AnyAsync(
                x => x.Name == normalizedName, cancellationToken);
            if (duplicateName)
            {
                throw new InvalidDataException($"A profile named '{normalizedName}' already exists.");
            }

            profile = new SubtitleLanguageProfile
            {
                Name = normalizedName,
                CutoffPosition = cutoffPosition
            };
            db.SubtitleLanguageProfiles.Add(profile);
        }

        for (var i = 0; i < normalizedItems.Length; i++)
        {
            db.SubtitleLanguageProfileItems.Add(new SubtitleLanguageProfileItem
            {
                ProfileId = profile.Id,
                LanguageTag = normalizedItems[i].LanguageTag,
                Forced = normalizedItems[i].Forced,
                Sdh = normalizedItems[i].Sdh,
                SortOrder = i
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        // First profile ever created becomes the global default so resolution never returns nothing.
        var hasGlobalDefault = await db.SubtitleProfileAssignments
            .AnyAsync(a => a.MediaType == null && a.LibraryRootId == null, cancellationToken);
        if (!hasGlobalDefault)
        {
            db.SubtitleProfileAssignments.Add(new SubtitleProfileAssignment { ProfileId = profile.Id });
            await db.SaveChangesAsync(cancellationToken);
        }

        return profile.Id;
    }

    public async Task<bool> DeleteAsync(Guid profileId, CancellationToken cancellationToken)
    {
        var inUse = await db.SubtitleProfileAssignments.AnyAsync(a => a.ProfileId == profileId, cancellationToken);
        if (inUse)
        {
            return false;
        }

        var removed = await db.SubtitleLanguageProfiles
            .Where(x => x.Id == profileId)
            .ExecuteDeleteAsync(cancellationToken);

        if (removed == 0)
        {
            return false;
        }

        await db.SubtitleLanguageProfileItems
            .Where(x => x.ProfileId == profileId)
            .ExecuteDeleteAsync(cancellationToken);
        return true;
    }

    public Task SetGlobalDefaultAsync(Guid profileId, CancellationToken cancellationToken) =>
        SetAssignmentAsync(null, null, profileId, cancellationToken);

    /// <summary>Null <paramref name="profileId"/> clears the override, falling back to the global default.</summary>
    public Task AssignMediaTypeAsync(
        WatchlistMediaType mediaType,
        Guid? profileId,
        CancellationToken cancellationToken) =>
        SetAssignmentAsync(mediaType, null, profileId, cancellationToken);

    /// <summary>Null <paramref name="profileId"/> clears the override, falling back to the media-type/global default.</summary>
    public Task AssignLibraryRootAsync(
        Guid libraryRootId,
        Guid? profileId,
        CancellationToken cancellationToken) =>
        SetAssignmentAsync(null, libraryRootId, profileId, cancellationToken);

    /// <summary>
    /// Resolves the profile for a media type / library root, falling back
    /// root+type -&gt; root -&gt; media type -&gt; global default, bootstrapping a default profile
    /// the first time nothing has been configured at all.
    /// </summary>
    public async Task<SubtitleProfileResolution> ResolveAsync(
        WatchlistMediaType mediaType,
        Guid? libraryRootId,
        CancellationToken cancellationToken)
    {
        await EnsureBootstrapAsync(cancellationToken);

        var assignments = await db.SubtitleProfileAssignments
            .AsNoTracking()
            .Where(a =>
                (a.MediaType == mediaType || a.MediaType == null) &&
                (libraryRootId == null || a.LibraryRootId == libraryRootId || a.LibraryRootId == null))
            .ToListAsync(cancellationToken);

        var profileId =
            assignments.FirstOrDefault(a => a.MediaType == mediaType && a.LibraryRootId == libraryRootId)?.ProfileId
            ?? assignments.FirstOrDefault(a => a.MediaType == null && a.LibraryRootId == libraryRootId)?.ProfileId
            ?? assignments.FirstOrDefault(a => a.MediaType == mediaType && a.LibraryRootId == null)?.ProfileId
            ?? assignments.FirstOrDefault(a => a.MediaType == null && a.LibraryRootId == null)?.ProfileId;

        SubtitleLanguageProfile? profile = profileId is Guid id
            ? await db.SubtitleLanguageProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            : null;

        profile ??= await db.SubtitleLanguageProfiles.AsNoTracking().OrderBy(x => x.Name).FirstAsync(cancellationToken);

        var items = await db.SubtitleLanguageProfileItems
            .AsNoTracking()
            .Where(x => x.ProfileId == profile.Id)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(cancellationToken);

        return new SubtitleProfileResolution(profile, items);
    }

    private async Task SetAssignmentAsync(
        WatchlistMediaType? mediaType,
        Guid? libraryRootId,
        Guid? profileId,
        CancellationToken cancellationToken)
    {
        var existing = await db.SubtitleProfileAssignments
            .Where(a => a.MediaType == mediaType && a.LibraryRootId == libraryRootId)
            .ToListAsync(cancellationToken);

        db.SubtitleProfileAssignments.RemoveRange(existing);

        if (profileId is Guid id)
        {
            var exists = await db.SubtitleLanguageProfiles.AnyAsync(x => x.Id == id, cancellationToken);
            if (!exists)
            {
                throw new InvalidDataException("Subtitle language profile does not exist.");
            }

            db.SubtitleProfileAssignments.Add(new SubtitleProfileAssignment
            {
                MediaType = mediaType,
                LibraryRootId = libraryRootId,
                ProfileId = id
            });
        }
        else if (mediaType is null && libraryRootId is null)
        {
            throw new InvalidDataException("The global default profile cannot be cleared.");
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Creates a "Default" profile from the resolved learning content language (#365) the first
    /// time no profile exists at all, so every other operation always has something to resolve to.
    /// </summary>
    private async Task EnsureBootstrapAsync(CancellationToken cancellationToken)
    {
        if (await db.SubtitleLanguageProfiles.AnyAsync(cancellationToken))
        {
            return;
        }

        await BootstrapGate.WaitAsync(cancellationToken);
        try
        {
            if (await db.SubtitleLanguageProfiles.AnyAsync(cancellationToken))
            {
                return;
            }

            var contentLanguage = await new LearningContentLanguageResolver(db)
                .ResolveTargetLanguageAsync(cancellationToken);

            var profile = new SubtitleLanguageProfile { Name = DefaultProfileName };
            db.SubtitleLanguageProfiles.Add(profile);
            db.SubtitleLanguageProfileItems.Add(new SubtitleLanguageProfileItem
            {
                ProfileId = profile.Id,
                LanguageTag = contentLanguage,
                SortOrder = 0
            });
            db.SubtitleProfileAssignments.Add(new SubtitleProfileAssignment { ProfileId = profile.Id });

            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            BootstrapGate.Release();
        }
    }
}
