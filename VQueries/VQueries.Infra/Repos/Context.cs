using Microsoft.EntityFrameworkCore;
using Dotseed.Context;
using MediatR;

using CombineQueries.Infra.Configures;
using CombineQueries.Domain.Aggregates.Translator;
using CombineQueries.Domain.Aggregates.Account;

namespace CombineQueries.Infra.Repos;

//without event-bus
public class Context : UnitOfWorkContext
{
    public DbSet<Translator> Translators { get; set; }
    public DbSet<Account> Accounts { get; set; }
    public Context(DbContextOptions options, IMediator mediator) : base(options, mediator) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Translator>().Ignore(t => t.Runes);

        modelBuilder.ApplyConfiguration(new AccountEntityConfiguration());

        // modelBuilder.ApplyConfiguration(new TranslatorEntityConfiguration());
    }

    public override async Task<bool> SaveEntitiesAsync(CancellationToken cancellationToken = new CancellationToken())
    {
        await base.SaveEntitiesAsync(cancellationToken);

        // await _integrationEventLogService.PublishStoredIntegrationEventsAsync();

        return true;
    }
}