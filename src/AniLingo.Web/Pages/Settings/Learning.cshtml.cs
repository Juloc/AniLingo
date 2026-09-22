using System.ComponentModel.DataAnnotations;
using AniLingo.Web.Features.Learning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AniLingo.Web.Pages.Settings;

public sealed class LearningModel(LearningService learningService) : PageModel
{
    [BindProperty]
    [Range(80, 97)]
    public int DesiredRetentionPercent { get; set; } = 90;

    [BindProperty]
    [Range(5, 200)]
    public int ReviewBatchSize { get; set; } = LearningPreferences.DefaultReviewBatchSize;

    [BindProperty]
    [Range(0, 100)]
    public int NewWordsPerDay { get; set; } = LearningPreferences.DefaultNewWordsPerDay;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var preferences = await learningService.GetPreferencesAsync(cancellationToken);
        DesiredRetentionPercent = (int)Math.Round(preferences.DesiredRetention * 100);
        ReviewBatchSize = preferences.ReviewBatchSize;
        NewWordsPerDay = preferences.NewWordsPerDay;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        await learningService.SavePreferencesAsync(
            DesiredRetentionPercent / 100d,
            ReviewBatchSize,
            NewWordsPerDay,
            cancellationToken);

        TempData["Status"] = "Learning settings saved.";
        return RedirectToPage();
    }
}
