using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using CombineQueries.Domain.Aggregates.Translator;

namespace CombineQueries.Infra.Configures;

// Зависимая сущность агрегата Translator, устроена как VirtualFragmentEntityConfiguration.
public class HyperEntityConfiguration : IEntityTypeConfiguration<Hyper>
{
    public void Configure(EntityTypeBuilder<Hyper> builder)
    {
        // Ключ составной: handle нумеруется внутри своего транслятора, а не глобально.
        // Как и у VirtualFragment: без DbSet имя таблицы взялось бы от типа ("Hyper").
        builder.ToTable("Hypers");

        builder.HasKey(h => new { h.TranslatorId, h.Id });

        // handle назначает Speech по порядку интернирования - identity бы его перебил.
        builder.Property(h => h.Id).ValueGeneratedNever();

        builder.Property(h => h.Url).IsRequired().HasMaxLength(Hyper.UrlMax);

        // handle<->url биекция: один URL не может висеть на двух handle.
        builder.HasIndex(h => new { h.TranslatorId, h.Url }).IsUnique();

        builder
            .HasOne<Translator>()
            .WithMany(t => t.Hypers)
            .HasForeignKey(h => h.TranslatorId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
