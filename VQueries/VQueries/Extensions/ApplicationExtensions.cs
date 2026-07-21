using CombineQueries.Api.Services.AFST;

namespace CombineQueries.Api.Extensions;

public static class ApplicationExtensions
{
    public static IServiceCollection ConfigureApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<HttpClient>(); // yes, i know about D from SOLID and it is broken this principle. But is not comers code, maybe fix later
        services.AddScoped<IAFST, AFST>();

        return services;
    }
}