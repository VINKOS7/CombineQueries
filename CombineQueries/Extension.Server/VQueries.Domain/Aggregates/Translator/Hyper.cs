using Dotseed.Domain;

namespace CombineQueries.Domain.Aggregates.Translator;

// Хайпер: handle -> уже собранный URL. Собрав URL по рунам один раз, клиент дальше зовёт его одним
// запросом /h/<handle> - это самый дешёвый путь, дешевле любого фрагмента.
//
// Часть агрегата Translator, как и VirtualFragment: своего репозитория нет, живёт через него.
// Персист нужен по той же причине - в памяти handle не переживает гашение хоста.
public class Hyper : Entity
{
    // handle. Назначает Speech по порядку интернирования, уникален в пределах транслятора.
    public new int Id { get; set; }

    public Guid TranslatorId { get; set; }

    public required string Url { get; set; }

    public const int UrlMax = 2048;
}
