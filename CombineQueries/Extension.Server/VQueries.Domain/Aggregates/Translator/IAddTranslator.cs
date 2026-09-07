using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Domain.Aggregates.Translator;

public interface IAddTranslator<TSymbol> where TSymbol : notnull
{
    public string Alphabet { get; }
    public IArenaTreeRunes<TSymbol> Runes { get; set; }

    public string BaseForwardUrl { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public int SizeRune { get; set; }
}
