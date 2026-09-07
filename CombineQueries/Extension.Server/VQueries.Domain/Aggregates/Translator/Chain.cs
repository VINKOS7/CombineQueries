using Dotseed.Domain;

namespace CombineQueries.Domain.Aggregates.Translator;

// Узел дерева цепочек: одна позиция в потоке combine-запросов.
//
// Дерево, а не граф переходов: узел это позиция в КОНКРЕТНОЙ ветке, а не сам фрагмент. Если хранить
// переходы как «фрагмент A -> фрагмент B», цепочки A→B→C и X→B→D склеятся в общем B, и по началу
// A→B вернутся оба продолжения. Прыжок обязан восстанавливать ровно один путь, поэтому у узла есть
// родитель: B из первой ветки и B из второй - разные строки.
//
// Часть агрегата Translator, как VirtualFragment и Hyper: своего репозитория нет.
public class Chain : Entity
{
    // Он же номер прыжка (/h/<id>): клиент называет узел, сервер восстанавливает путь до него.
    // Уникален в пределах транслятора - отсюда составной ключ.
    public new int Id { get; set; }

    public Guid TranslatorId { get; set; }

    // null - ребёнок корня. Ссылка на ту же таблицу: дерево строится ей.
    public int? ParentId { get; set; }

    // Ребро: "f<адрес>" для фрагмента, "r<руны>" для чанка.
    public required string Step { get; set; }

    // Заполнен только у листа - собранный этой цепочкой адрес. У промежуточных узлов пусто.
    public string? Url { get; set; }

    public const int StepMax = 64;

    public const int UrlMax = 2048;
}
