using System.ComponentModel.DataAnnotations;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Settings;

public sealed class LearningModel(
    AppDbContext db,
    LearningService learningService,
    CurrentAccountContext currentAccount) : PageModel
{
    public static IReadOnlyList<LearningCapabilityOption> CapabilityOptions { get; } =
    [
        new(LearningCapability.LanguageLookup, "settings.learning.capability.languageLookup.label", "settings.learning.capability.languageLookup.description"),
        new(LearningCapability.ReadingAids, "settings.learning.capability.readingAids.label", "settings.learning.capability.readingAids.description"),
        new(LearningCapability.Translation, "settings.learning.capability.translation.label", "settings.learning.capability.translation.description"),
        new(LearningCapability.AiExplanations, "settings.learning.capability.aiExplanations.label", "settings.learning.capability.aiExplanations.description"),
        new(LearningCapability.Vocabulary, "settings.learning.capability.vocabulary.label", "settings.learning.capability.vocabulary.description"),
        new(LearningCapability.Reviews, "settings.learning.capability.reviews.label", "settings.learning.capability.reviews.description"),
        new(LearningCapability.SentencePractice, "settings.learning.capability.sentencePractice.label", "settings.learning.capability.sentencePractice.description"),
        new(LearningCapability.ScriptTrainer, "settings.learning.capability.scriptTrainer.label", "settings.learning.capability.scriptTrainer.description"),
        new(LearningCapability.Progress, "settings.learning.capability.progress.label", "settings.learning.capability.progress.description"),
        new(LearningCapability.HomeWidget, "settings.learning.capability.homeWidget.label", "settings.learning.capability.homeWidget.description"),
        new(LearningCapability.ContentMetrics, "settings.learning.capability.contentMetrics.label", "settings.learning.capability.contentMetrics.description"),
        new(LearningCapability.PreparationSuggestions, "settings.learning.capability.preparationSuggestions.label", "settings.learning.capability.preparationSuggestions.description"),
        new(LearningCapability.PlayerTools, "settings.learning.capability.playerTools.label", "settings.learning.capability.playerTools.description"),
        new(LearningCapability.ReaderTools, "settings.learning.capability.readerTools.label", "settings.learning.capability.readerTools.description")
    ];

    public static IReadOnlyList<LearningMediaType> MediaTypes { get; } =
        Enum.GetValues<LearningMediaType>();

    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    [BindProperty]
    public LearningMode Mode { get; set; }

    [BindProperty]
    [Range(80, 97)]
    public int DesiredRetentionPercent { get; set; } = 90;

    [BindProperty]
    [Range(5, 200)]
    public int ReviewBatchSize { get; set; } = LearningPreferences.DefaultReviewBatchSize;

    [BindProperty]
    [Range(0, 100)]
    public int NewWordsPerDay { get; set; } = LearningPreferences.DefaultNewWordsPerDay;

    public LearningScopeSettingsSnapshot Global { get; private set; } =
        Empty(LearningScopeRef.Profile);

    public IReadOnlyDictionary<LearningMediaType, LearningScopeSettingsSnapshot> Media { get; private set; } =
        new Dictionary<LearningMediaType, LearningScopeSettingsSnapshot>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        var configuration = new LearningConfigurationStore(db);

        await configuration.SetModeAsync(
            currentAccount.ProfileId,
            LearningScopeRef.Profile,
            Mode,
            cancellationToken);

        foreach (var capability in Enum.GetValues<LearningCapability>())
        {
            await configuration.SetCapabilityOverrideAsync(
                currentAccount.ProfileId,
                LearningScopeRef.Profile,
                capability,
                ParseOverride(Request.Form[$"cap_{capability}"].FirstOrDefault()),
                cancellationToken);
        }

        foreach (var mediaType in MediaTypes)
        {
            await configuration.SetModeAsync(
                currentAccount.ProfileId,
                LearningScopeRef.ForMedia(mediaType),
                ParseModeOverride(Request.Form[$"mediaMode_{mediaType}"].FirstOrDefault()),
                cancellationToken);

            foreach (var capability in Enum.GetValues<LearningCapability>())
            {
                await configuration.SetCapabilityOverrideAsync(
                    currentAccount.ProfileId,
                    LearningScopeRef.ForMedia(mediaType),
                    capability,
                    ParseOverride(Request.Form[$"media_{mediaType}_{capability}"].FirstOrDefault()),
                    cancellationToken);
            }
        }

        await learningService.SavePreferencesAsync(
            DesiredRetentionPercent / 100d,
            ReviewBatchSize,
            NewWordsPerDay,
            cancellationToken);

        TempData["Status"] = Ui["settings.learning.saved"];
        return RedirectToPage();
    }

    public IEnumerable<SelectListItem> OverrideOptions(
        bool? current,
        string inheritLabel) =>
    [
        new(inheritLabel, "inherit", current is null),
        new(Ui["settings.learning.on"], "on", current == true),
        new(Ui["settings.learning.off"], "off", current == false)
    ];

    public IEnumerable<SelectListItem> ModeOverrideOptions(
        LearningMode? current) =>
    [
        new(Ui["settings.learning.media.inherit"], "inherit", current is null),
        new(Ui["settings.learning.mode.off"], "Off", current == LearningMode.Off),
        new(Ui["settings.learning.mode.languageTools"], "LanguageTools", current == LearningMode.LanguageTools),
        new(Ui["settings.learning.mode.study"], "Study", current == LearningMode.Study),
        new(Ui["settings.learning.mode.custom"], "Custom", current == LearningMode.Custom)
    ];

    public string ModeName(LearningMode mode) =>
        mode switch
        {
            LearningMode.Off => Ui["settings.learning.mode.off"],
            LearningMode.LanguageTools => Ui["settings.learning.mode.languageTools"],
            LearningMode.Study => Ui["settings.learning.mode.study"],
            LearningMode.Custom => Ui["settings.learning.mode.custom"],
            _ => mode.ToString()
        };

    public string ModeDescription(LearningMode mode) =>
        mode switch
        {
            LearningMode.Off => Ui["settings.learning.mode.off.description"],
            LearningMode.LanguageTools => Ui["settings.learning.mode.languageTools.description"],
            LearningMode.Study => Ui["settings.learning.mode.study.description"],
            LearningMode.Custom => Ui["settings.learning.mode.custom.description"],
            _ => string.Empty
        };

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        var configuration = new LearningConfigurationStore(db);
        Global = await configuration.GetScopeAsync(
            currentAccount.ProfileId,
            LearningScopeRef.Profile,
            cancellationToken);

        Mode = Global.ModeOverride ?? LearningMode.Off;

        var media = new Dictionary<LearningMediaType, LearningScopeSettingsSnapshot>();
        foreach (var mediaType in MediaTypes)
        {
            media[mediaType] = await configuration.GetScopeAsync(
                currentAccount.ProfileId,
                LearningScopeRef.ForMedia(mediaType),
                cancellationToken);
        }

        Media = media;

        var preferences = await learningService.GetPreferencesAsync(cancellationToken);
        DesiredRetentionPercent = (int)Math.Round(preferences.DesiredRetention * 100);
        ReviewBatchSize = preferences.ReviewBatchSize;
        NewWordsPerDay = preferences.NewWordsPerDay;
    }

    private static bool? ParseOverride(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "on" => true,
            "off" => false,
            _ => null
        };

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

    private static LearningScopeSettingsSnapshot Empty(
        LearningScopeRef scope) =>
        new(
            scope,
            null,
            Enum.GetValues<LearningCapability>()
                .ToDictionary(x => x, _ => (bool?)null));
}

public sealed record LearningCapabilityOption(
    LearningCapability Capability,
    string LabelKey,
    string DescriptionKey);
