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

        // Алфавит и есть идентичность транслятора: по нему его ищет connect (GetByAlphabetAsync).
        // Без уникальности заводятся дубли, и FirstOrDefault цепляет какой попало - в проде уже
        // ловили пустышку вместо словаря. Настраиваем здесь, а не в TranslatorEntityConfiguration:
        // та не дописана (кидает NotImplementedException) и не подключена.
        modelBuilder.Entity<Translator>().HasIndex(t => t.Alphabet).IsUnique();

        modelBuilder.ApplyConfiguration(new AccountEntityConfiguration());

        // Своих DbSet у VirtualFragment и Hyper нет намеренно: это части агрегата Translator и
        // достаются только через его навигации.
        modelBuilder.ApplyConfiguration(new VirtualFragmentEntityConfiguration());
        modelBuilder.ApplyConfiguration(new HyperEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ChainEntityConfiguration());

        // modelBuilder.ApplyConfiguration(new TranslatorEntityConfiguration());
    }

    public override async Task<bool> SaveEntitiesAsync(CancellationToken cancellationToken = new CancellationToken())
    {
        await base.SaveEntitiesAsync(cancellationToken);

        // await _integrationEventLogService.PublishStoredIntegrationEventsAsync();

        return true;
    }
}