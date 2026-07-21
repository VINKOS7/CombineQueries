using CombineQueries.Infra.Repos.TranslatorRepo;
using CombineQueries.Domain.Aggregates.Translator;

namespace CombineQueries.Api.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection ConfigureInfrastructureServices(this IServiceCollection services)
    {
        services.AddScoped<ITranslatorRepo, TranslatorRepo>();

        return services;
    }
}
