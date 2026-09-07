namespace CombineQueries.Api.Services.Speech;

// Хайпер как растущее дерево цепочек запросов, а не плоский список handle -> url.
//
// Слой = позиция в цепочке: корень это «ничего не пришло», глубина N - «пришло N кусков».
// Ребро - один запрос (руна или фрагмент по адресу), путь от корня - весь поток до хвоста.
//
// Смысл дерева в ПРЕФИКСАХ: два url с общим началом занимают общие узлы, и повтор такого начала
// можно проскочить одним /h/, а дальше досылать только расхождение. Плоский список так не умеет -
// в нём повторно используется лишь адрес целиком.
public class HyperTree
{
    private readonly Node _root = new();

    // Узлы, которым выдан номер: индекс в списке и есть handle. Номер получают только полезные -
    // лист (весь url) и последний общий узел (докуда можно прыгнуть у похожего адреса).
    private readonly List<Node> _handles = [];

    public int Chains { get; private set; }

    public int Nodes { get; private set; } = 1;

    public int Deepest { get; private set; }

    public int Handles => _handles.Count;

    // Ключ ребра: у фрагмента это адрес, у руны - сами руны. Буква впереди разводит их, чтобы
    // фрагмент с адресом 5 не столкнулся с руной "5".
    public static string StepOf(bool isFragment, string rune, int fragmentId) =>
        isFragment ? "f" + fragmentId : "r" + rune;

    // Кладёт цепочку целиком.
    //
    // Leaf - номер листа: прыжок на весь url. Prefix - номер последнего узла, который СУЩЕСТВОВАЛ
    // до этой вставки: докуда путь совпал с уже известными. Shared - его глубина.
    public (int Leaf, int Prefix, int Shared) Remember(IReadOnlyList<string> steps, string url)
    {
        var node = _root;
        var shared = _root;
        int depth = 0;
        bool fresh = false;

        foreach (string step in steps)
        {
            if (node.Next.TryGetValue(step, out var next))
            {
                if (!fresh) { shared = next; depth++; }
            }
            else
            {
                next = new Node { Parent = node, Step = step };

                node.Next[step] = next;

                Nodes++;
                fresh = true;

                // Новый узел: номер выдаём сразу, чтобы было что сохранить и чем прыгать.
                _pending.Add((HandleOf(next), node.Handle < 0 ? null : node.Handle, step, null));
            }

            node = next;
        }

        if (steps.Count > Deepest) Deepest = steps.Count;

        if (node.Url is null) Chains++;

        bool changed = node.Url != url;

        node.Url = url;

        // Лист получил адрес - это тоже изменение, его надо донести до персиста. Повтор того же
        // адреса ничего не меняет и в запись не идёт: иначе каждый прыжок тянул бы лишний UPDATE.
        if (changed)
            _pending.Add((HandleOf(node), node.Parent is null || node.Parent.Handle < 0 ? null : node.Parent.Handle, node.Step ?? "", url));

        // Префикс полезен, только если он короче самой цепочки: иначе прыжок покрывает всё и
        // отдельный номер ему не нужен.
        int prefix = depth > 0 && depth < steps.Count ? HandleOf(shared) : -1;

        return (HandleOf(node), prefix, depth);
    }

    // Путь до узла, шагами от корня. По нему сборка восстанавливается с середины.
    public IReadOnlyList<string>? PathOf(int handle)
    {
        if (handle < 0 || handle >= _handles.Count) return null;

        var steps = new List<string>();

        for (var node = _handles[handle]; node.Parent is not null; node = node.Parent) steps.Add(node.Step!);

        steps.Reverse();

        return steps;
    }

    public string? UrlOf(int handle) => handle >= 0 && handle < _handles.Count ? _handles[handle].Url : null;

    // Сколько адресов ещё возможно после такого префикса. -1 - префикс не встречался.
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

    // Узлы, появившиеся с прошлого сбора: их и надо сохранить. Список чистится сбором.
    public IReadOnlyList<(int Id, int? ParentId, string Step, string? Url)> TakePending()
    {
        var pending = _pending.ToArray();

        _pending.Clear();

        return pending;
    }

    private readonly List<(int Id, int? ParentId, string Step, string? Url)> _pending = [];

    // Поднимает дерево из персиста. Номера сохраняются - иначе выданные клиенту прыжки протухнут.
    public void Restore(IEnumerable<(int Id, int? ParentId, string Step, string? Url)> nodes)
    {
        Forget();

        var byId = new Dictionary<int, Node>();
        var rows = nodes.OrderBy(n => n.Id).ToList();

        // Сначала заводим все узлы, потом связываем: родитель может встретиться позже ребёнка.
        foreach (var row in rows)
        {
            var node = new Node { Step = row.Step, Url = row.Url, Handle = row.Id };

            byId[row.Id] = node;

            while (_handles.Count <= row.Id) _handles.Add(_root);

            _handles[row.Id] = node;

            if (row.Url is not null) Chains++;
        }

        foreach (var row in rows)
        {
            var node = byId[row.Id];
            var parent = row.ParentId is int pid && byId.TryGetValue(pid, out var found) ? found : _root;

            node.Parent = parent;
            parent.Next[row.Step] = node;
        }

        Nodes = 1 + rows.Count;

        foreach (var node in byId.Values)
        {
            int depth = 0;

            for (var at = node; at.Parent is not null; at = at.Parent) depth++;

            if (depth > Deepest) Deepest = depth;
        }

        // Восстановленное уже лежит в базе - на запись не отдаём.
        _pending.Clear();
    }

    // Все листы: адрес -> номер прыжка. Это и есть сид хайпера для клиента.
    public IEnumerable<(string Url, int Jump)> Leaves()
    {
        for (int i = 0; i < _handles.Count; i++)
            if (_handles[i].Url is not null && _handles[i].Handle == i)
                yield return (_handles[i].Url!, i);
    }

    public void Forget()
    {
        _root.Next.Clear();
        _root.Url = null;

        _handles.Clear();
        _pending.Clear();

        Chains = 0;
        Nodes = 1;
        Deepest = 0;
    }

    private int HandleOf(Node node)
    {
        if (node.Handle >= 0) return node.Handle;

        node.Handle = _handles.Count;

        _handles.Add(node);

        return node.Handle;
    }

    private static int Leaves(Node node)
    {
        int count = node.Url is null ? 0 : 1;

        foreach (var child in node.Next.Values) count += Leaves(child);

        return count;
    }

    private sealed class Node
    {
        public readonly Dictionary<string, Node> Next = [];

        public Node? Parent;

        public string? Step;

        public string? Url;

        public int Handle = -1;
    }
}
