using Microsoft.EntityFrameworkCore;

using MediatR;

using CombineQueries.Infra.Repos;

namespace CombineQueries.Api.Extensions;

public static class EntityFrameworkCoreExtensions
{
    public static IServiceCollection ConfigureEntityFramework(this IServiceCollection services, IConfiguration configuration, bool sensitiveLogging = false)
    {
        string? connectionString = configuration.GetConnectionString("Context");
        services
            .AddEntityFrameworkNpgsql()
            .AddDbContext<Context>(
                options => options
                    .UseNpgsql(
                        connectionString,
                        b => b.MigrationsAssembly(typeof(Program).Assembly.GetName().Name)
                    )
                    .EnableSensitiveDataLogging(sensitiveLogging)
            );

        return services;
    }

    public static void RunMigrations(this WebApplication app, IConfiguration configuration)
    {
        var mediator = app.Services.GetRequiredService<IMediator>();
        var logger = app.Services.GetRequiredService<ILogger<Context>>();
        string? connectionString = configuration.GetConnectionString("Context");

        var options = new DbContextOptionsBuilder<Context>()
            .UseNpgsql(
                connectionString,
                b => b
                    .MigrationsAssembly(typeof(Program).Assembly.GetName().Name)
                    .MigrationsHistoryTable(
                        "__EFMigrationsHistory",
                "public"))
            .Options;

        using var context = new Context(options, mediator);

        try
        {
            context.Database.Migrate();

            logger.LogInformation("db: migrations applied");
        }
        catch (Exception ex)
        {
            logger.LogWarning("db: migrations skipped, running without persistence ({Kind}: {Message})", ex.GetType().Name, ex.Message);
        }
    }
}