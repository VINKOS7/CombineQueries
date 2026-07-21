using static System.Net.Mime.MediaTypeNames;

namespace CombineQueries.Domain.Aggregates.Translator.types;

public interface IArenaTreeRunes<TSymbol>
{
    public IRune<TSymbol>? Root { get; }
    public IRune<TSymbol> Get(int id);
    public IRune<TSymbol> From(IRune<TSymbol> move, TSymbol symbol);
}

public class ArenaTreeRunes<TSymbol> : IArenaTreeRunes<TSymbol>
{
    private readonly List<IRune<TSymbol>> _runes = new();
    public IRune<TSymbol>? Root { get; }

    public IRune<TSymbol> From(IRune<TSymbol> move, TSymbol symbol)
    {
        if (move.Next.TryGetValue(symbol, out var childId)) return _runes[childId];

        var node = new Rune<TSymbol>
        {
            Id = _runes.Count,
            ParentId = move.Id,
            ParentSymbol = symbol
        };

        _runes.Add(node);

        move.Next[symbol] = node.Id;

        return node;
    }

    public IRune<TSymbol>? Get(int id) => _runes[id];
}
