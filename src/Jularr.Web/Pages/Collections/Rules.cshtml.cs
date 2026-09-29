using Jularr.Web.Data;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Collections;
using Jularr.Web.Features.Localization;
using Jularr.Web.Features.MediaCore;
using Jularr.Web.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Jularr.Web.Pages.Collections;

public sealed class RulesModel(
    AppDbContext db,
    CurrentAccountContext account,
    CollectionService collections) : PageModel
{
    public UiTextBundle Ui { get; private set; } = UiTextBundle.English;

    public Collection Collection { get; private set; } = null!;

    /// <summary>The current rule tree serialized for the client builder; "null" when none yet.</summary>
    public string RuleJson => Collection.RuleJson ?? "null";

    /// <summary>Field option metadata for the builder: value, label and which operators/value editor apply.</summary>
    public static IReadOnlyList<RuleFieldOption> Fields { get; } =
    [
        new("mediaType", "Media type", "enum", MediaTypeValues, ["equals", "notEquals"]),
        new("status", "Status", "enum", StatusValues, ["equals", "notEquals"]),
        new("releaseYear", "Release year", "number", [], NumberOperators),
        new("primaryUnitCount", "Episodes/chapters", "number", [], NumberOperators),
        new("secondaryUnitCount", "Seasons/volumes", "number", [], NumberOperators),
        new("language", "Language (any)", "text", [], ["contains", "notContains"]),
        new("languageComplete", "Language (complete)", "text", [], ["contains", "notContains"]),
        new("hasFranchise", "Part of a franchise", "none", [], ["isPresent", "isAbsent"]),
        new("franchise", "Franchise id", "text", [], ["contains", "notContains"])
    ];

    private static string[] NumberOperators =>
        ["equals", "notEquals", "greaterThan", "greaterThanOrEqual", "lessThan", "lessThanOrEqual", "isPresent", "isAbsent"];

    private static IReadOnlyList<RuleOptionValue> MediaTypeValues =>
    [
        .. WorkMediaTypes.All.Select(type => new RuleOptionValue(WorkMediaTypes.ToStorage(type), type.ToString()))
    ];

    private static IReadOnlyList<RuleOptionValue> StatusValues =>
    [
        .. Enum.GetNames<MediaReleaseStatus>().Select(name => new RuleOptionValue(name.ToLowerInvariant(), name))
    ];

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        Ui = await UiRequestLocalization.GetBundleAsync(HttpContext, db);
        var collection = await collections.GetAsync(account.ProfileId, id, cancellationToken);
        if (collection is null || collection.Kind != CollectionKind.Smart)
        {
            return NotFound();
        }

        Collection = collection;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(
        Guid id,
        string? name,
        string? description,
        string? rulesJson,
        CancellationToken cancellationToken)
    {
        var collection = await collections.GetAsync(account.ProfileId, id, cancellationToken);
        if (collection is null || collection.Kind != CollectionKind.Smart)
        {
            return NotFound();
        }

        var rule = CollectionRuleSerializer.Deserialize(rulesJson);
        if (rule is null)
        {
            TempData["Status"] = "The rule could not be read. Add at least one condition.";
            return RedirectToPage(new { id });
        }

        await collections.UpdateAsync(
            account.ProfileId,
            id,
            string.IsNullOrWhiteSpace(name) ? collection.Name : name,
            description,
            rule,
            cancellationToken);
        TempData["Status"] = "Rule saved.";
        return RedirectToPage("/Collections/Detail", new { id });
    }

    public sealed record RuleFieldOption(
        string Value,
        string Label,
        string Editor,
        IReadOnlyList<RuleOptionValue> Values,
        IReadOnlyList<string> Operators);

    public sealed record RuleOptionValue(string Value, string Label);
}
