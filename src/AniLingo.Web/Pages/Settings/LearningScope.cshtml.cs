using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Learning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Settings;

public sealed class LearningScopeModel(
    AppDbContext db,
    CurrentAccountContext currentAccount) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public LearningMediaType MediaType { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Scope { get; set; } = "work";

    [BindProperty(SupportsGet = true)]
    public string? WorkKey { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ContentKey { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Label { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty]
    public string ModeOverride { get; set; } = "inherit";

    public LearningScopeSettingsSnapshot Current { get; private set; } =
        new(
            LearningScopeRef.Profile,
            null,
            Enum.GetValues<LearningCapability>()
                .ToDictionary(x => x, _ => (bool?)null));

    public LearningResolvedSettings Effective { get; private set; } =
        new(
            LearningMode.Off,
            LearningConfigurationDefaults.For(LearningMode.Off));

    public IReadOnlyList<LearningCapability> Capabilities { get; } =
        Enum.GetValues<LearningCapability>();

    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(Label)
            ? Scope.Equals("content", StringComparison.OrdinalIgnoreCase)
                ? "This item"
                : $"This {MediaType.ToString().ToLowerInvariant()}"
            : Label.Trim();

    public async Task<IActionResult> OnGetAsync(
        CancellationToken cancellationToken)
    {
        if (!TryBuildScopeReference(
                MediaType,
                Scope,
                WorkKey,
                ContentKey,
                out var scopeRef))
        {
            return BadRequest();
        }

        var store = new LearningConfigurationStore(db);
        Current = await store.GetScopeAsync(
            currentAccount.ProfileId,
            scopeRef,
            cancellationToken);
        Effective = await store.ResolveAsync(
            currentAccount.ProfileId,
            BuildContext(MediaType, WorkKey, ContentKey),
            cancellationToken);
        ModeOverride = Current.ModeOverride?.ToString() ?? "inherit";

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(
        CancellationToken cancellationToken)
    {
        if (!TryBuildScopeReference(
                MediaType,
                Scope,
                WorkKey,
                ContentKey,
                out var scopeRef))
        {
            return BadRequest();
        }

        var store = new LearningConfigurationStore(db);
        await store.SetModeAsync(
            currentAccount.ProfileId,
            scopeRef,
            ParseModeOverride(ModeOverride),
            cancellationToken);

        foreach (var capability in Capabilities)
        {
            await store.SetCapabilityOverrideAsync(
                currentAccount.ProfileId,
                scopeRef,
                capability,
                ParseCapabilityOverride(
                    Request.Form[$"cap_{capability}"].FirstOrDefault()),
                cancellationToken);
        }

        TempData["Status"] = $"Learning settings saved for {DisplayLabel}.";

        if (!string.IsNullOrWhiteSpace(ReturnUrl)
            && Url.IsLocalUrl(ReturnUrl))
        {
            return LocalRedirect(ReturnUrl);
        }

        return RedirectToPage(
            new
            {
                mediaType = MediaType,
                scope = Scope,
                workKey = WorkKey,
                contentKey = ContentKey,
                label = Label,
                returnUrl = ReturnUrl
            });
    }

    public string CapabilityLabel(LearningCapability capability) =>
        capability switch
        {
            LearningCapability.LanguageLookup => "Word lookup",
            LearningCapability.ReadingAids => "Reading aids",
            LearningCapability.Translation => "Translation",
            LearningCapability.AiExplanations => "AI explanations",
            LearningCapability.Vocabulary => "Vocabulary",
            LearningCapability.Reviews => "Spaced repetition",
            LearningCapability.SentencePractice => "Sentence practice",
            LearningCapability.ScriptTrainer => "Writing-system trainer",
            LearningCapability.Progress => "Learning progress",
            LearningCapability.HomeWidget => "Home learning widget",
            LearningCapability.ContentMetrics => "Learning metrics",
            LearningCapability.PreparationSuggestions => "Pre-study suggestions",
            LearningCapability.PlayerTools => "Player language tools",
            LearningCapability.ReaderTools => "Reader language tools",
            _ => capability.ToString()
        };

    public string CapabilityDescription(LearningCapability capability) =>
        capability switch
        {
            LearningCapability.LanguageLookup =>
                "Look up words without automatically adding them to reviews.",
            LearningCapability.ReadingAids =>
                "Readings, transliteration and script help when supported.",
            LearningCapability.Translation =>
                "Offer translations for selected text or subtitles.",
            LearningCapability.AiExplanations =>
                "Optional contextual grammar/meaning explanations.",
            LearningCapability.Vocabulary =>
                "Allow saving and managing vocabulary.",
            LearningCapability.Reviews =>
                "Allow active spaced-repetition cards for this scope.",
            LearningCapability.SentencePractice =>
                "Use sentences from this content in practice.",
            LearningCapability.ScriptTrainer =>
                "Enable writing-system practice when the language supports it.",
            LearningCapability.Progress =>
                "Include this scope in Learning progress.",
            LearningCapability.HomeWidget =>
                "Allow this scope to contribute to the optional Home Learning widget.",
            LearningCapability.ContentMetrics =>
                "Show known/prepared metrics on content pages.",
            LearningCapability.PreparationSuggestions =>
                "Suggest preparing vocabulary before consuming this content.",
            LearningCapability.PlayerTools =>
                "Show compact language tools in the media player.",
            LearningCapability.ReaderTools =>
                "Show compact language tools in the reader.",
            _ => string.Empty
        };

    public static bool TryBuildScopeReference(
        LearningMediaType mediaType,
        string? scope,
        string? workKey,
        string? contentKey,
        out LearningScopeRef scopeRef)
    {
        if (string.Equals(
                scope,
                "content",
                StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(contentKey))
        {
            scopeRef = LearningScopeRef.ForContent(
                mediaType,
                contentKey);
            return true;
        }

        if ((string.IsNullOrWhiteSpace(scope)
                || string.Equals(
                    scope,
                    "work",
                    StringComparison.OrdinalIgnoreCase))
            && !string.IsNullOrWhiteSpace(workKey))
        {
            scopeRef = LearningScopeRef.ForWork(
                mediaType,
                workKey);
            return true;
        }

        scopeRef = LearningScopeRef.Profile;
        return false;
    }

    private static LearningScopeContext BuildContext(
        LearningMediaType mediaType,
        string? workKey,
        string? contentKey) =>
        new(
            mediaType,
            string.IsNullOrWhiteSpace(workKey) ? null : workKey.Trim(),
            string.IsNullOrWhiteSpace(contentKey) ? null : contentKey.Trim());

    private static LearningMode? ParseModeOverride(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Equals("inherit", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Enum.TryParse<LearningMode>(
            value,
            ignoreCase: true,
            out var parsed)
            ? parsed
            : null;
    }

    private static bool? ParseCapabilityOverride(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "on" => true,
            "off" => false,
            _ => null
        };
}
