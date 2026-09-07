using Dotseed.Domain;

namespace CombineQueries.Domain.Aggregates.Translator;

// Строка словаря транслятора: ГЛОБАЛЬНЫЙ адрес -> текст. Адрес тот же, которым живёт рантайм
// (Speech._fragments): [0, dfaSize) - L2, [dfaSize, dfaSize*pageCount) - L3 (id = page*dfaSize + offset),
// дальше финитных адресов нет - Infinite.
//
// Часть агрегата Translator, не корень: своего репозитория нет, живёт только через Translator.
// Персист словаря нужен потому, что в памяти он не переживает гашение хоста (холодный старт).
public class VirtualFragment : Entity
{
    // Адрес назначает словарь, а не база. Уникален в пределах транслятора - отсюда составной ключ.
    public new int Id { get; set; }

    public Guid TranslatorId { get; set; }

    public required string Text { get; set; }

    public FragmentLevel Level { get; set; }

    // Infinite: следующее звено цепи (+1 запрос на хоп). null = хвост цепи.
    public int? Jump { get; set; }

    // Infinite: готовый результат, забирается на query.tail, чтобы не проходить цепь заново.
    public string? End { get; set; }

    public const int TextMax = 512;

    // Уровень по адресу: ниже dfaSize - одна развязка, до ёмкости - две, дальше адресов нет.
    public static FragmentLevel LevelOf(int id, int dfaSize, int pageCount)
    {
        if (dfaSize <= 0 || id < 0) return FragmentLevel.Infinite;

        if (id < dfaSize) return FragmentLevel.L2;

        return id < dfaSize * (pageCount < 1 ? 1 : pageCount) ? FragmentLevel.L3 : FragmentLevel.Infinite;
    }
}
