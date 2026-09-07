namespace CombineQueries.Api.Services.Speech;

// Хайпер как растущее дерево цепочек запросов, а не плоский список handle -> url.
//
// Слой = позиция в цепочке: корень это «ничего не пришло», глубина N - «пришло N кусков».
// Ребро - один запрос (руна или фрагмент по адресу), путь от корня - весь поток до хвоста.
// Общий префикс двух url занимает общие узлы, а не дублируется: одинаково начинающиеся адреса
// делят начало дерева, и различаются лишь там, где реально расходятся.
//
// Зачем: по префиксу видно, сколько адресов ещё возможно. Если после k кусков лист единственный -
// продолжение известно заранее, и это открывает дорогу к досрочному форварду. Пока только копим и
// измеряем; персист в БД - следующим шагом, сейчас всё живёт в памяти.
public class HyperTree
{
    private readonly Node _root = new();

    public int Chains { get; private set; }

    public int Nodes { get; private set; } = 1;

    public int Deepest { get; private set; }

    // Ключ ребра: у фрагмента это его адрес, у руны - сами руны. Префиксы «f»/«r» разводят их,
    // чтобы фрагмент с адресом 5 не столкнулся с руной "5".
    public static string StepOf(bool isFragment, string rune, int fragmentId) =>
        isFragment ? "f" + fragmentId : "r" + rune;

    // Кладёт цепочку целиком. Возвращает, была ли она уже известна.
    public bool Remember(IReadOnlyList<string> steps, string url)
    {
        var node = _root;

        foreach (string step in steps)
        {
            if (!node.Next.TryGetValue(step, out var next))
            {
                next = new Node();

                node.Next[step] = next;

                Nodes++;
            }

            node = next;
        }

        if (steps.Count > Deepest) Deepest = steps.Count;

        bool known = node.Url is not null;

        node.Url = url;

        if (!known) Chains++;

        return known;
    }

    // Сколько адресов ещё возможно после такого префикса и известен ли он однозначно.
    // -1 значит «префикс не встречался»; 1 - продолжение единственное.
    public int Ahead(IReadOnlyList<string> prefix)
    {
        var node = _root;

        foreach (string step in prefix)
        {
            if (!node.Next.TryGetValue(step, out var next)) return -1;

            node = next;
        }

        return Leaves(node);
    }

    // Url, если префикс однозначно ведёт к единственной цепочке.
    public string? Only(IReadOnlyList<string> prefix)
    {
        var node = _root;

        foreach (string step in prefix)
        {
            if (!node.Next.TryGetValue(step, out var next)) return null;

            node = next;
        }

        return Single(node);
    }

    public void Forget()
    {
        _root.Next.Clear();
        _root.Url = null;

        Chains = 0;
        Nodes = 1;
        Deepest = 0;
    }

    private static int Leaves(Node node)
    {
        int count = node.Url is null ? 0 : 1;

        foreach (var child in node.Next.Values) count += Leaves(child);

        return count;
    }

    private static string? Single(Node node)
    {
        if (node.Next.Count == 0) return node.Url;

        // Ветвление или лист по дороге - однозначности нет.
        if (node.Next.Count > 1 || node.Url is not null) return null;

        foreach (var child in node.Next.Values) return Single(child);

        return null;
    }

    private sealed class Node
    {
        public readonly Dictionary<string, Node> Next = [];

        public string? Url;
    }
}
