using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using CombineQueries.Domain.Aggregates.Translator;

namespace CombineQueries.Infra.Configures;

// Зависимая сущность агрегата Translator. Связь настраиваем отсюда: TranslatorEntityConfiguration
// не дописана (кидает NotImplementedException) и в Context не подключена - её не трогаем.
public class VirtualFragmentEntityConfiguration : IEntityTypeConfiguration<VirtualFragment>
{
    public void Configure(EntityTypeBuilder<VirtualFragment> builder)
    {
        // Ключ составной: адрес развязки уникален не глобально, а в пределах своего транслятора.
        // Имя таблицы задаём явно: DbSet у этой сущности нет (она внутри агрегата), поэтому EF взял
        // бы имя ТИПА - "VirtualFragment" в единственном числе, вразрез с Accounts/Translators.
        builder.ToTable("VirtualFragments");

        builder.HasKey(f => new { f.TranslatorId, f.Id });

        // Адрес назначает словарь (page*dfaSize + offset) - identity бы его перебил.
        builder.Property(f => f.Id).ValueGeneratedNever();

        builder.Property(f => f.Text).IsRequired().HasMaxLength(VirtualFragment.TextMax);
        builder.Property(f => f.Level).IsRequired();
        builder.Property(f => f.End).HasMaxLength(VirtualFragment.TextMax);

        // Словарь - биекция текст<->адрес, дубль текста означал бы два адреса на одну строку.
        builder.HasIndex(f => new { f.TranslatorId, f.Text }).IsUnique();
        builder.HasIndex(f => new { f.TranslatorId, f.Level });

        builder
            .HasOne<Translator>()
            .WithMany(t => t.VirtualFragments)
            .HasForeignKey(f => f.TranslatorId)
            .OnDelete(DeleteBehavior.Cascade);

        // HasData НЕТ намеренно: сид уехал бы и в прод. На релизе таблица пустая, наполняет её только
        // /preheat; тестовые строки кладёт dev-сид (DevFragmentSeed) - под Development.
    }
}
