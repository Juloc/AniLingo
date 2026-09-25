using AniLingo.Web.Features.Localization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AniLingo.Web.Pages.Account;

/// <summary>
/// Shared rules and localized validation messages for the anonymous account
/// pages. DataAnnotations still decide validity; the displayed message comes
/// from the UI catalog in the request language.
/// </summary>
internal static class AccountForm
{
    public const int MaxUserNameLength = 80;
    public const int MinPasswordLength = 12;

    public static string UserNameMessage(UiTextBundle ui) =>
        ui.Format("account.validation.userName", ("max", MaxUserNameLength));

    public static string NewPasswordMessage(UiTextBundle ui) =>
        ui.Format("account.validation.newPassword", ("min", MinPasswordLength));

    public static void LocalizeFieldErrors(
        ModelStateDictionary modelState,
        IReadOnlyDictionary<string, string> messages)
    {
        foreach (var (field, message) in messages)
        {
            if (modelState.TryGetValue(field, out var entry) && entry.Errors.Count > 0)
            {
                entry.Errors.Clear();
                entry.Errors.Add(message);
            }
        }
    }

    /// <summary>
    /// Maps account-creation failures from <c>OwnerAuthService</c> to a
    /// localized field or form message.
    /// </summary>
    public static void AddCreationError(
        ModelStateDictionary modelState,
        UiTextBundle ui,
        Exception exception)
    {
        switch (exception)
        {
            case ArgumentException { ParamName: "userName" }:
                modelState.AddModelError("UserName", UserNameMessage(ui));
                break;
            case ArgumentException { ParamName: "password" }:
                modelState.AddModelError("Password", NewPasswordMessage(ui));
                break;
            case InvalidOperationException:
                modelState.AddModelError(string.Empty, ui["account.error.userNameTaken"]);
                break;
            default:
                modelState.AddModelError(string.Empty, exception.Message);
                break;
        }
    }
}
