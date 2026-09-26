using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Settings;

public sealed class LearningScopeModel(
    AppDbContext db,
    CurrentAccountContext currentAccount) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

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
                ? Ui["settings.learningScope.thisItem"]
                : Ui.Format("settings.learningScope.thisMediaType", ("mediaType", MediaType.ToString().ToLowerInvariant()))
            : Label.Trim();

    public async Task<IActionResult> OnGetAsync(
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

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
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

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

        TempData["Status"] = Ui.Format("settings.learningScope.saved", ("label", DisplayLabel));

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

    public string ModeName(LearningMode mode) =>
        mode switch
        {
            LearningMode.Off => Ui["settings.learning.mode.off"],
            LearningMode.LanguageTools => Ui["settings.learning.mode.languageTools"],
            LearningMode.Study => Ui["settings.learning.mode.study"],
            LearningMode.Custom => Ui["settings.learning.mode.custom"],
            _ => mode.ToString()
        };

    public string CapabilityLabel(LearningCapability capability) =>
        capability switch
        {
            LearningCapability.LanguageLookup => Ui["settings.learning.capability.languageLookup.label"],
            LearningCapability.ReadingAids => Ui["settings.learning.capability.readingAids.label"],
            LearningCapability.Translation => Ui["settings.learning.capability.translation.label"],
            LearningCapability.AiExplanations => Ui["settings.learning.capability.aiExplanations.label"],
            LearningCapability.Vocabulary => Ui["settings.learning.capability.vocabulary.label"],
            LearningCapability.Reviews => Ui["settings.learning.capability.reviews.label"],
            LearningCapability.SentencePractice => Ui["settings.learning.capability.sentencePractice.label"],
            LearningCapability.ScriptTrainer => Ui["settings.learning.capability.scriptTrainer.label"],
            LearningCapability.Progress => Ui["settings.learning.capability.progress.label"],
            LearningCapability.HomeWidget => Ui["settings.learning.capability.homeWidget.label"],
            LearningCapability.ContentMetrics => Ui["settings.learningScope.capability.contentMetrics.label"],
            LearningCapability.PreparationSuggestions => Ui["settings.learning.capability.preparationSuggestions.label"],
            LearningCapability.PlayerTools => Ui["settings.learning.capability.playerTools.label"],
            LearningCapability.ReaderTools => Ui["settings.learning.capability.readerTools.label"],
            _ => capability.ToString()
        };

    public string CapabilityDescription(LearningCapability capability) =>
        capability switch
        {
            LearningCapability.LanguageLookup =>
                Ui["settings.learningScope.capability.languageLookup.description"],
            LearningCapability.ReadingAids =>
                Ui["settings.learningScope.capability.readingAids.description"],
            LearningCapability.Translation =>
                Ui["settings.learningScope.capability.translation.description"],
            LearningCapability.AiExplanations =>
                Ui["settings.learningScope.capability.aiExplanations.description"],
            LearningCapability.Vocabulary =>
                Ui["settings.learningScope.capability.vocabulary.description"],
            LearningCapability.Reviews =>
                Ui["settings.learningScope.capability.reviews.description"],
            LearningCapability.SentencePractice =>
                Ui["settings.learningScope.capability.sentencePractice.description"],
            LearningCapability.ScriptTrainer =>
                Ui["settings.learningScope.capability.scriptTrainer.description"],
            LearningCapability.Progress =>
                Ui["settings.learningScope.capability.progress.description"],
            LearningCapability.HomeWidget =>
                Ui["settings.learningScope.capability.homeWidget.description"],
            LearningCapability.ContentMetrics =>
                Ui["settings.learningScope.capability.contentMetrics.description"],
            LearningCapability.PreparationSuggestions =>
                Ui["settings.learningScope.capability.preparationSuggestions.description"],
            LearningCapability.PlayerTools =>
                Ui["settings.learningScope.capability.playerTools.description"],
            LearningCapability.ReaderTools =>
                Ui["settings.learningScope.capability.readerTools.description"],
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
