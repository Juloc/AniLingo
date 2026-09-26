using AniLingo.Web.Features.Auth;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace AniLingo.Web.Features.Acquisition.Api;

/// <summary>
/// The owner-only acquisition automation API (<c>/api/acquisition/v1</c>): monitoring, wanted
/// episodes, search-now, operations/history, manual imports and a health summary, for a script or
/// external automation client. Authenticated with an <c>X-Api-Key</c> header (see
/// <see cref="AcquisitionApiKeyAuthenticationHandler"/>) or a cookie-based owner session, both
/// required to hold the owner role; every write goes through <see cref="AcquisitionApiService"/>,
/// which itself only calls the same services the owner Razor Pages call.
/// </summary>
public static class AcquisitionApiEndpoints
{
    public static IEndpointRouteBuilder MapAcquisitionApiV1(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup(AcquisitionApiRoutes.BasePath)
            .RequireAuthorization(policy => policy
                .AddAuthenticationSchemes(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    AcquisitionApiKeyAuthenticationHandler.SchemeName)
                .RequireRole(AccountRoles.Owner))
            .RequireRateLimiting("acquisitionApi");

        group.MapGet("/monitored", async (
            AcquisitionApiService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetMonitoredAsync(cancellationToken)));

        group.MapGet("/anime/{animeId:guid}/monitoring", (
            Guid animeId,
            AcquisitionApiService service,
            CancellationToken cancellationToken) =>
            RunAsync(() => service.GetAnimeMonitoringAsync(animeId, cancellationToken)));

        group.MapPut("/anime/{animeId:guid}/monitoring", (
            Guid animeId,
            SetAnimeMonitoringRequest request,
            AcquisitionApiService service,
            CancellationToken cancellationToken) =>
            RunAsync(() => service.SetAnimeMonitoringAsync(animeId, request, cancellationToken)));

        group.MapGet("/wanted", (
            Guid? animeId,
            AcquisitionApiService service,
            CancellationToken cancellationToken) =>
            RunAsync(() => service.GetWantedAsync(animeId, cancellationToken)));

        group.MapPost("/search", (
            AcquisitionApiService service,
            CancellationToken cancellationToken) =>
            RunAsync(() => service.SearchAllMonitoredAsync(cancellationToken), StatusCodes.Status202Accepted));

        group.MapPost("/anime/{animeId:guid}/search", (
            Guid animeId,
            AcquisitionApiService service,
            CancellationToken cancellationToken) =>
            RunAsync(() => service.SearchAnimeAsync(animeId, cancellationToken), StatusCodes.Status202Accepted));

        group.MapGet("/operations", (
            string? status,
            string? kind,
            int? limit,
            AcquisitionApiService service,
            CancellationToken cancellationToken) =>
            RunAsync(() => service.ListOperationsAsync(status, kind, limit ?? 50, cancellationToken)));

        group.MapGet("/operations/{operationId:guid}", (
            Guid operationId,
            AcquisitionApiService service,
            CancellationToken cancellationToken) =>
            RunAsync(() => service.GetOperationAsync(operationId, cancellationToken)));

        group.MapGet("/history", (
            Guid? animeId,
            int? limit,
            AcquisitionApiService service,
            CancellationToken cancellationToken) =>
            RunAsync(() => service.GetHistoryAsync(animeId, limit ?? 30, cancellationToken)));

        group.MapGet("/imports", (
            bool? attention,
            AcquisitionApiService service,
            CancellationToken cancellationToken) =>
            RunAsync(() => service.GetManualImportsAsync(attention ?? false, cancellationToken)));

        group.MapPost("/imports/{recordId:guid}/resolve", (
            Guid recordId,
            ResolveManualImportRequest request,
            AcquisitionApiService service,
            CancellationToken cancellationToken) =>
            RunAsync(() => service.ResolveManualImportAsync(recordId, request, cancellationToken)));

        group.MapPost("/imports/{recordId:guid}/dismiss", (
            Guid recordId,
            AcquisitionApiService service,
            CancellationToken cancellationToken) =>
            RunAsync(() => service.DismissManualImportAsync(recordId, cancellationToken)));

        group.MapGet("/health", (
            AcquisitionApiService service,
            CancellationToken cancellationToken) =>
            RunAsync(() => service.GetHealthSummaryAsync(cancellationToken)));

        return endpoints;
    }

    /// <summary>
    /// Every endpoint funnels through here so an <see cref="AcquisitionApiException"/> from the
    /// service (not found / Sonarr ownership refusal / validation) always becomes the same
    /// problem-details shape instead of each handler duplicating a try/catch.
    /// </summary>
    private static async Task<IResult> RunAsync<T>(Func<Task<T>> action, int successStatusCode = StatusCodes.Status200OK)
    {
        try
        {
            var result = await action();
            return successStatusCode == StatusCodes.Status200OK
                ? Results.Ok(result)
                : Results.Json(result, statusCode: successStatusCode);
        }
        catch (AcquisitionApiException exception)
        {
            return Results.Problem(
                detail: exception.Detail,
                statusCode: exception.StatusCode,
                title: exception.Title);
        }
    }
}
