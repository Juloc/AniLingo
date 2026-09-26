using System.ComponentModel.DataAnnotations;
using System.Globalization;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Learning.Courses;
using AniLingo.Web.Features.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Pages.Settings;

public sealed class LearningCoursesModel(
    AppDbContext db,
    CurrentAccountContext currentAccount) : PageModel
{
    private LearningCourseStore Store { get; } = new(db);

    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;
    public IReadOnlyList<LearningCourseSnapshot> Courses { get; private set; } = [];
    public IReadOnlyDictionary<Guid, int> WordCounts { get; private set; } =
        new Dictionary<Guid, int>();

    [BindProperty]
    public NewCourseInput NewCourse { get; set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        if (ModelState.IsValid)
        {
            try
            {
                var course = await Store.CreateAsync(
                    currentAccount.ProfileId,
                    NewCourse.SourceLanguage,
                    NewCourse.TargetLanguage,
                    NewCourse.Name,
                    new LearningCourseOptions(
                        NewCourse.Recognition,
                        NewCourse.Production,
                        NewCourse.Listening,
                        NewCourse.Writing,
                        NewCourse.SentencePractice),
                    cancellationToken);

                TempData["Status"] = Ui.Format("settings.learningCourses.created", ("name", course.Name));
                return RedirectToPage();
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                ModelState.AddModelError(string.Empty, exception.Message);
            }
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostUpdateAsync(
        Guid courseId,
        string? name,
        bool isEnabled,
        bool recognition,
        bool production,
        bool listening,
        bool writing,
        bool sentencePractice,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        try
        {
            await Store.UpdateAsync(
                currentAccount.ProfileId,
                courseId,
                name,
                isEnabled,
                new LearningCourseOptions(
                    recognition,
                    production,
                    listening,
                    writing,
                    sentencePractice),
                cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException exception)
        {
            TempData["Status"] = exception.Message;
            return RedirectToPage();
        }

        TempData["Status"] = Ui["settings.learningCourses.saved"];
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPrimaryAsync(
        Guid courseId,
        CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        try
        {
            await Store.SetPrimaryAsync(currentAccount.ProfileId, courseId, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }

        TempData["Status"] = Ui["settings.learningCourses.primaryChanged"];
        return RedirectToPage();
    }

    public static string LanguageName(string languageTag)
    {
        try
        {
            return CultureInfo.GetCultureInfo(languageTag).NativeName;
        }
        catch (CultureNotFoundException)
        {
            return languageTag;
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);

        Courses = await Store.ListAsync(currentAccount.ProfileId, cancellationToken);
        WordCounts = await LearningQueries.WordCards(db, currentAccount.ProfileId)
            .GroupBy(x => x.CourseId)
            .Select(group => new { CourseId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.CourseId, x => x.Count, cancellationToken);
    }

    public sealed class NewCourseInput
    {
        [Required]
        [StringLength(LearningCourseModelConfiguration.LanguageTagMaxLength)]
        [Display(Name = "Source language")]
        public string SourceLanguage { get; set; } = "";

        [Required]
        [StringLength(LearningCourseModelConfiguration.LanguageTagMaxLength)]
        [Display(Name = "Target language")]
        public string TargetLanguage { get; set; } = "";

        [StringLength(LearningCourseStore.NameMaxLength)]
        public string? Name { get; set; }

        public bool Recognition { get; set; } = true;
        public bool Production { get; set; }
        public bool Listening { get; set; }
        public bool Writing { get; set; }
        public bool SentencePractice { get; set; } = true;
    }
}
