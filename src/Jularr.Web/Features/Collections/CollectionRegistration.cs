namespace Jularr.Web.Features.Collections;

/// <summary>
/// Registers the smart/manual collections feature (#427). All services are scoped over the request's
/// <c>AppDbContext</c>, matching the rest of the application; the assembled-board cache is a static inside
/// <see cref="CollectionService"/> so it is shared across requests like the discovery shelf cache.
/// </summary>
public static class CollectionRegistration
{
    public static IServiceCollection AddCollections(this IServiceCollection services)
    {
        services.AddScoped<CollectionStore>();
        services.AddScoped<CollectionFactsProvider>();
        services.AddScoped<CollectionService>();
        return services;
    }
}
