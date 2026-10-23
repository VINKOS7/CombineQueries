// Copy of the formula from Translator.UnrunedCombine (tunnelValue = nodeId*(L+1)+symbolIndex) + encode,
// which doesn't exist in the real code at all (only decode is there). Standalone folder, doesn't touch the repo.

class Rune
{
    public required int Id;
    public int ParentId = -1;
    public char ParentSymbol;
    public Dictionary<char, int> Next = new();
}

class ArenaTree
{
    private readonly List<Rune> _runes = new();
    public Rune Root;

    public ArenaTree()
    {
        Root = new Rune { Id = 0 };
        _runes.Add(Root);
    }

    public Rune Get(int id) => _runes[id];

    public Rune From(Rune move, char symbol)
    {
        if (move.Next.TryGetValue(symbol, out var childId)) return _runes[childId];

        var node = new Rune { Id = _runes.Count, ParentId = move.Id, ParentSymbol = symbol };
        _runes.Add(node);
        move.Next[symbol] = node.Id;
        return node;
    }

    public int Count => _runes.Count;
}

static class ArenaTreeFactory
{
    public static ArenaTree ATRFrom(string alphabet)
    {
        var arena = new ArenaTree();
        foreach (char c in alphabet) arena.From(arena.Root, c);
        return arena;
    }
}

record DecodeResult(long TunnelValue, int NodeId, int SymbolIndex, string PrefixFromTree, char? GrownSymbol);

// Клиент - держит своё зеркало дерева и позицию в сообщении, отдаёт по одному tunnel-запросу за раз.
class Client
{
    private readonly string _alphabet;
    private readonly ArenaTree _tree;
    private readonly string _message;
    private int _pos;

    public Client(string alphabet, string message)
    {
        _alphabet = alphabet;
        _tree = ArenaTreeFactory.ATRFrom(alphabet);
        _message = message;
        _pos = 0;
    }

    public Client(string alphabet, ArenaTree tree, string message)
    {
        _alphabet = alphabet;
        _tree = tree;
        _message = message;
        _pos = 0;
    }

    public bool IsDone => _pos >= _message.Length;
    public int TreeNodeCount => _tree.Count;

    // жадно матчит текущую позицию вниз по дереву, при необходимости растит на 1 символ,
    // pos двигается ровно на глубину матча (НЕ +1 за расширение - иначе теряется символ)
    public string? NextRequest()
    {
        if (IsDone) return null;

        var node = _tree.Root;
        int depth = 0;

        while (_pos + depth < _message.Length && node.Next.TryGetValue(_message[_pos + depth], out int childId))
        {
            node = _tree.Get(childId);
            depth++;
        }

        int symbolIndex;

        if (_pos + depth < _message.Length)
        {
            char nextChar = _message[_pos + depth];
            symbolIndex = _alphabet.IndexOf(nextChar);
            _tree.From(node, nextChar);
        }
        else
        {
            symbolIndex = _alphabet.Length; // sentinel
        }

        int symbolSpan = _alphabet.Length + 1;
        long tunnelValue = (long)node.Id * symbolSpan + symbolIndex;

        _pos += depth;

        return ToBaseAlphabet(tunnelValue, _alphabet);
    }

    private static string ToBaseAlphabet(long value, string alphabet)
    {
        int baseLen = alphabet.Length;
        if (value == 0) return alphabet[0].ToString();

        var digits = new List<char>();
        while (value > 0)
        {
            digits.Add(alphabet[(int)(value % baseLen)]);
            value /= baseLen;
        }
        digits.Reverse();
        return new string(digits.ToArray());
    }
}

// Сервер - держит своё зеркало дерева, сам дёргает клиента за следующим запросом и раскодирует его.
class Server
{
    private readonly string _alphabet;
    private readonly ArenaTree _tree;
    private string _accumulated = "";

    public Server(string alphabet)
    {
        _alphabet = alphabet;
        _tree = ArenaTreeFactory.ATRFrom(alphabet);
    }

    public Server(string alphabet, ArenaTree tree)
    {
        _alphabet = alphabet;
        _tree = tree;
    }

    public string Accumulated => _accumulated;
    public int TreeNodeCount => _tree.Count;
    public int LastRounds { get; private set; }
    public int LastTunnelChars { get; private set; }

    // сервер сам зовёт client.NextRequest() пока клиенту есть что слать, раунд за раундом
    public void ProcessAll(Client client, bool verbose = true)
    {
        int round = 0;
        _accumulated = "";
        LastTunnelChars = 0;

        while (!client.IsDone)
        {
            round++;

            string? tunnel = client.NextRequest();
            if (tunnel is null) break;

            var result = DecodeVerbose(tunnel);
            _accumulated += result.PrefixFromTree;
            LastTunnelChars += tunnel.Length;

            if (verbose)
            {
                Console.WriteLine($"  round {round}:");
                Console.WriteLine($"server received: \"{tunnel}\"");
                Console.WriteLine($"server unruned: tunnelValue={result.TunnelValue}, nodeId={result.NodeId}, symbolIndex={result.SymbolIndex}, prefixFromTree=\"{result.PrefixFromTree}\", grown={(result.GrownSymbol is null ? "none/sentinel" : $"'{result.GrownSymbol}'")}, accumulatedSoFar=\"{_accumulated}\"");
            }
        }

        LastRounds = round;
    }

    // то же самое, что Translator.UnrunedCombine, но с раскрытием всех промежуточных шагов
    private DecodeResult DecodeVerbose(string tunnelRunes)
    {
        long tunnelValue = 0;

        foreach (var c in tunnelRunes)
        {
            int digit = _alphabet.IndexOf(c);
            if (digit < 0) throw new Exception($"domain error: symbol '{c}' not in alphabet");
            tunnelValue = tunnelValue * _alphabet.Length + digit;
        }

        int symbolSpan = _alphabet.Length + 1;
        int nodeId = (int)(tunnelValue / symbolSpan);
        int symbolIndex = (int)(tunnelValue % symbolSpan);

        var node = _tree.Get(nodeId);

        var chars = new Stack<char>();
        var walk = node;

        while (walk.ParentId != -1)
        {
            chars.Push(walk.ParentSymbol);
            walk = _tree.Get(walk.ParentId);
        }

        string prefixFromTree = new string(chars.ToArray());

        char? grown = null;

        if (symbolIndex < _alphabet.Length)
        {
            grown = _alphabet[symbolIndex];
            _tree.From(node, grown.Value);
        }

        return new DecodeResult(tunnelValue, nodeId, symbolIndex, prefixFromTree, grown);
    }
}
