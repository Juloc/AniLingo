namespace Jularr.Web.Features.Learning.Curriculum;

/// <summary>
/// Registers the Learning v3 curriculum foundation services (#441). Both are scoped
/// over the request's <c>AppDbContext</c>, matching the rest of the Learning feature.
/// </summary>
public static class CurriculumRegistration
{
    public static IServiceCollection AddLearningCurriculum(this IServiceCollection services)
    {
        services.AddScoped<CurriculumBlueprintService>();
        services.AddScoped<SharedCourseService>();
        return services;
    }
}
