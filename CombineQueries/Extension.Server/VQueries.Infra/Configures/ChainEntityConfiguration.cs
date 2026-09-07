using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using CombineQueries.Domain.Aggregates.Translator;

namespace CombineQueries.Infra.Configures;

// Зависимая сущность агрегата Translator, устроена как VirtualFragment и Hyper.
public class ChainEntityConfiguration : IEntityTypeConfiguration<Chain>
{
    public void Configure(EntityTypeBuilder<Chain> builder)
    {
        // Имя явно: DbSet у сущности нет, иначе EF взял бы имя типа в единственном числе.
        builder.ToTable("Chains");

        // Ключ составной: номер узла нумеруется внутри своего транслятора, а не глобально.
        builder.HasKey(c => new { c.TranslatorId, c.Id });

        // Номер назначает дерево (он же номер прыжка) - identity бы его перебил.
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Step).IsRequired().HasMaxLength(Chain.StepMax);
        builder.Property(c => c.Url).HasMaxLength(Chain.UrlMax);

        // Дети одного родителя: по нему дерево и обходится при восстановлении.
        builder.HasIndex(c => new { c.TranslatorId, c.ParentId });

        builder
            .HasOne<Translator>()
            .WithMany(t => t.Chains)
            .HasForeignKey(c => c.TranslatorId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
