using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.OfflineLibrary;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Settings.Prefetch;

/// <summary>
/// Settings → Downloads → Smart prefetch (#415): the per-profile policy that lets the
/// server plan next-up offline downloads within a hard storage cap. Off by default.
/// The policy lives in <see cref="OfflinePrefetchPolicyStore"/>; the native client
/// fetches its plans through the client API.
/// </summary>
public sealed class IndexModel(
    AppDbContext db,
    OfflinePrefetchPolicyStore store,
    CurrentAccountContext currentAccount) : PageModel
{
    private const long MiB = 1024L * 1024L;
    private const long GiB = 1024L * MiB;

    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    [BindProperty]
    public PolicyInput Input { get; set; } = new();

    public string? Error { get; private set; }

    /// <summary>Cap presets, plus the stored cap when it is not one of them.</summary>
    public IReadOnlyList<long> CapChoices =>
        [.. OfflinePrefetchPolicy.CapChoices.Append(Input.CapBytes).Distinct().Order()];

    public int MaxEpisodesAhead => OfflinePrefetchPolicy.MaxEpisodesAhead;

    public int MaxChaptersAhead => OfflinePrefetchPolicy.MaxChaptersAhead;

    public static string FormatCap(long bytes) =>
        bytes % GiB == 0 ? $"{bytes / GiB} GB" : $"{bytes / MiB} MB";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var policy = await store.LoadAsync(currentAccount.ProfileId, cancellationToken);
        Input = PolicyInput.From(policy);
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        try
        {
            await store.SaveAsync(currentAccount.ProfileId, Input.ToPolicy(), cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            Error = exception.Message;
            Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
            return Page();
        }

        TempData["Status"] = "Prefetch settings saved.";
        return RedirectToPage();
    }

    public sealed class PolicyInput
    {
        public bool Enabled { get; set; }
        public long CapBytes { get; set; } = OfflinePrefetchPolicy.DefaultCapBytes;
        public bool IncludeEpisodes { get; set; } = true;
        public bool IncludeChapters { get; set; } = true;
        public int EpisodesAhead { get; set; } = OfflinePrefetchPolicy.Default.EpisodesAhead;
        public int ChaptersAhead { get; set; } = OfflinePrefetchPolicy.Default.ChaptersAhead;
        public bool AllowMetered { get; set; }

        public static PolicyInput From(OfflinePrefetchPolicy policy) =>
            new()
            {
                Enabled = policy.Enabled,
                CapBytes = policy.CapBytes,
                IncludeEpisodes = policy.IncludeEpisodes,
                IncludeChapters = policy.IncludeChapters,
                EpisodesAhead = policy.EpisodesAhead,
                ChaptersAhead = policy.ChaptersAhead,
                AllowMetered = policy.AllowMetered
            };

        public OfflinePrefetchPolicy ToPolicy() =>
            new()
            {
                Enabled = Enabled,
                CapBytes = CapBytes,
                IncludeEpisodes = IncludeEpisodes,
                IncludeChapters = IncludeChapters,
                EpisodesAhead = EpisodesAhead,
                ChaptersAhead = ChaptersAhead,
                AllowMetered = AllowMetered
            };
    }
}
