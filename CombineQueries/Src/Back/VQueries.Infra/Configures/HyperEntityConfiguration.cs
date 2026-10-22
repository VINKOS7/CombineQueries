using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using CombineQueries.Domain.Aggregates.Translator;
using CombineQueries.Domain.Aggregates.Translator.types;

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

        builder.Property(h => h.Url).IsRequired();

        // handle<->url биекция: один URL не может висеть на двух handle. Индекс по хэшу, а не по
        // самому url: строка btree-индекса Postgres ограничена ~2,7 КБ, а url теперь без потолка.
        builder.Property<string>("UrlHash").HasComputedColumnSql("md5(\"Url\")", stored: true);

        builder.HasIndex("TranslatorId", "UrlHash").IsUnique();

        builder
            .HasOne<Translator>()
            .WithMany(t => t.Hypers)
            .HasForeignKey(h => h.TranslatorId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
