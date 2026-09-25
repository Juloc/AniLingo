using System.ComponentModel.DataAnnotations;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Learning;
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
        new(LearningCapability.LanguageLookup, "Word lookup", "Tap words to see dictionary information without creating study cards."),
        new(LearningCapability.ReadingAids, "Reading aids", "Show readings, transliteration or script help when a language toolkit supports it."),
        new(LearningCapability.Translation, "Translation", "Offer translations while watching or reading."),
        new(LearningCapability.AiExplanations, "AI explanations", "Allow optional contextual explanations. Never required for normal playback or reading."),
        new(LearningCapability.Vocabulary, "Vocabulary", "Save and manage words independently from scheduled reviews."),
        new(LearningCapability.Reviews, "Spaced repetition", "Use scheduled review cards and FSRS."),
        new(LearningCapability.SentencePractice, "Sentence practice", "Practice sentences from content you actually watched or read."),
        new(LearningCapability.ScriptTrainer, "Writing-system trainer", "Show language-specific script practice such as Kana when supported."),
        new(LearningCapability.Progress, "Learning progress", "Show progress inside the Learning area."),
        new(LearningCapability.HomeWidget, "Home learning widget", "Show a small Learning card on Home. Off by default even in Study mode."),
        new(LearningCapability.ContentMetrics, "Learning metrics on content", "Show known/prepared percentages on normal media and reader surfaces."),
        new(LearningCapability.PreparationSuggestions, "Pre-study suggestions", "Suggest vocabulary preparation before watching or reading."),
        new(LearningCapability.PlayerTools, "Player language tools", "Enable the compact subtitle word/sentence inspector in the player."),
        new(LearningCapability.ReaderTools, "Reader language tools", "Enable the compact word/sentence inspector while reading.")
    ];

    public static IReadOnlyList<LearningMediaType> MediaTypes { get; } =
        Enum.GetValues<LearningMediaType>();

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

        TempData["Status"] = "Learning preferences saved.";
        return RedirectToPage();
    }

    public IEnumerable<SelectListItem> OverrideOptions(
        bool? current,
        string inheritLabel) =>
    [
        new(inheritLabel, "inherit", current is null),
        new("On", "on", current == true),
        new("Off", "off", current == false)
    ];

    public IEnumerable<SelectListItem> ModeOverrideOptions(
        LearningMode? current) =>
    [
        new("Inherit", "inherit", current is null),
        new("Off", "Off", current == LearningMode.Off),
        new("Language tools", "LanguageTools", current == LearningMode.LanguageTools),
        new("Study", "Study", current == LearningMode.Study),
        new("Custom", "Custom", current == LearningMode.Custom)
    ];

    public static string ModeDescription(LearningMode mode) =>
        mode switch
        {
            LearningMode.Off =>
                "Watch and read normally. Learning UI stays out of the way unless a lower scope explicitly enables it.",
            LearningMode.LanguageTools =>
                "Lookup, readings, translation and optional explanations without cards or review obligations.",
            LearningMode.Study =>
                "Vocabulary, reviews, sentence practice and progress, while Home/content metrics remain non-intrusive by default.",
            LearningMode.Custom =>
                "Start with everything off and explicitly enable only the capabilities you want.",
            _ => string.Empty
        };

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
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
    string Label,
    string Description);
