namespace CombineQueries.Domain.Aggregates.Translator.types;

public interface IArenaTreeRunes<TSymbol>
{
    public IRune<TSymbol>? Root { get; }
    public IRune<TSymbol> From(IRune<TSymbol> move, TSymbol symbol);
}

public class ArenaTreeRunes<TSymbol> : IArenaTreeRunes<TSymbol>
{
    private readonly List<IRune<TSymbol>> _runes = new();
    public IRune<TSymbol>? Root { get; }

    // БЕЗ этого Root == null и _runes пуст - всё разваливается на первом же From/Get.
    // Терялось уже не раз, не удалять.
    public ArenaTreeRunes()
    {
        // ParentSymbol у рута смысла не имеет (required, поэтому default); маркер рута - ParentId == -1
        Root = new Rune<TSymbol> { Id = 0, ParentSymbol = default };

        _runes.Add(Root);
    }

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
}
