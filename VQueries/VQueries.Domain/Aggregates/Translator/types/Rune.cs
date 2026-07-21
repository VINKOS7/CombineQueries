namespace CombineQueries.Domain.Aggregates.Translator.types;

public interface IRune<TSymbol> where TSymbol : notnull
{
    int Id { get; set; }
    int ParentId { get; set; }
    bool IsTerminal { get; set; }
    TSymbol? ParentSymbol { get; set; }
    IDictionary<TSymbol, int> Next { get; set; }
    void ResetNext();
}

public record Rune<TSymbol> : IRune<TSymbol> where TSymbol : notnull
{
    public required int Id { get; set; }
    public int ParentId { get; set; } = -1;
    public required TSymbol? ParentSymbol { get; set; }

    public IDictionary<TSymbol, int> Next { get; set; } = new Dictionary<TSymbol, int>();
    public bool IsTerminal { get; set; }

    public void ResetNext() => Next = new Dictionary<TSymbol, int>();
}