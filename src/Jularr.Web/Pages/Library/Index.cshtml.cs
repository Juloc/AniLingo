using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Collections;
using Jularr.Web.Features.Library;
using Jularr.Web.Features.Localization;
using Jularr.Web.Ui;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Pages.Library;

public sealed class IndexModel(
    AppDbContext db,
    CurrentAccountContext currentAccount,
    CollectionService collections,
    ILogger<IndexModel> logger) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    /// <summary>What the address asks for: section, sort, layout and filters.</summary>
    public LibraryBrowseQuery Query { get; private set; } = new();

    public LibraryPageState State { get; private set; } = LibraryPageState.Ready;

    /// <summary>The titles that pass the filters, in sort order, ready to render.</summary>
    public IReadOnlyList<LibraryCardView> Cards { get; private set; } = [];

    /// <summary>All titles in the library, before filtering.</summary>
    public int Total { get; private set; }

    public LibraryFacets Facets { get; private set; } = new(
        new Dictionary<LibraryProgressState, int>(),
        new Dictionary<LibraryAvailabilityState, int>(),
        0, [], [], [], [], []);

    public LibraryLanguagePreference Preference { get; private set; } = LibraryLanguagePreference.None;

    public IReadOnlyList<CollectionTileView> Collections { get; private set; } = [];

    /// <summary>Supporting details failed to load; the titles are still shown.</summary>
    public bool Degraded { get; private set; }

    public string CurrentHref => LibraryBrowse.Href(Query);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        Query = LibraryBrowse.Parse(key => Request.Query.TryGetValue(key, out var values)
            ? [.. values.Where(x => x is not null).Select(x => x!)]
            : []);

        try
        {
            if (Query.Collections)
            {
                Collections = await collections.ListTilesAsync(User, currentAccount.ProfileId, cancellationToken);
                State = LibraryBrowse.ResolveState(false, false, Collections.Count, Collections.Count);
                return;
            }

            await LoadTitlesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "The library page could not be loaded.");
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            State = LibraryPageState.Error;
        }
    }

    private async Task LoadTitlesAsync(CancellationToken cancellationToken)
    {
        var read = await new LibraryMediaCardQuery(db).GetAnimeEntriesAsync(
            currentAccount.ProfileId,
            cancellationToken);

        var degraded = read.Degraded;
        try
        {
            var preferences = await db.ProfilePlaybackPreferences
                .AsNoTracking()
                .Where(x => x.ProfileId == currentAccount.ProfileId)
                .Select(x => new { x.PreferredAudioLanguage, x.PreferredSubtitleLanguage })
                .SingleOrDefaultAsync(cancellationToken);
            if (preferences is not null)
            {
                Preference = LibraryLanguagePreference.From(
                    preferences.PreferredAudioLanguage,
                    preferences.PreferredSubtitleLanguage);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Only the preferred-language highlight and filter are lost.
            logger.LogWarning(exception, "The playback language preferences could not be read for the library page.");
            degraded = true;
        }

        var shown = LibraryBrowse.Apply(read.Entries, Query, Preference);
        Total = read.Entries.Count;
        Facets = LibraryBrowse.Facets([.. read.Entries], Preference);
        Cards = [.. shown.Select(entry => LibraryCardView.Create(entry, Preference, Ui))];
        Degraded = degraded;
        State = LibraryBrowse.ResolveState(false, degraded, Total, Cards.Count);
    }
}
