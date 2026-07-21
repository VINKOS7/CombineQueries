using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Domain.Aggregates.Translator;

public interface IAddTranslator<TSymbol>
{
    public string Alphabet { get; }
    public IArenaTreeRunes<TSymbol> Runes { get; set; }

    public string BaseForwardUrl { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }
}
