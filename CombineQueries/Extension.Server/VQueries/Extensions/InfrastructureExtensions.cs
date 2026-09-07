using CombineQueries.Infra.Repos.TranslatorRepo;
using CombineQueries.Infra.Repos.AccountRepo;
using CombineQueries.Domain.Aggregates.Translator;
using CombineQueries.Domain.Aggregates.Account;

namespace CombineQueries.Api.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection ConfigureInfrastructureServices(this IServiceCollection services)
    {
        services.AddScoped<ITranslatorRepo, TranslatorRepo>();
        services.AddScoped<IAccountRepo, AccountRepo>();

        return services;
    }
}
