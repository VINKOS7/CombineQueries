using CombineQueries.Api.Services.AFST;

namespace CombineQueries.Api.Extensions;

public static class ApplicationExtensions
{
    public static IServiceCollection ConfigureApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<HttpClient>(); // yes, i know about D from SOLID and it is broken this principle. But is not comers code, maybe fix later

        // ТОЛЬКО Singleton. AFST держит состояние МЕЖДУ запросами (алфавит, дерево, буфер склейки),
        // а Scoped создаёт новый экземпляр на каждый HTTP-запрос - тогда /init ставит контекст и он
        // тут же теряется, и первый же /m/ падает с "CRIT: /init не вызван".
        services.AddSingleton<IAFST, AFST>();

        return services;
    }
}