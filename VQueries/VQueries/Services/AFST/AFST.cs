using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Services.AFST;

public class AFST : IAFST
{
    public string? Alphabet { get; private set; }
    public IArenaTreeRunes<char>? ArenaTreeContext { get; private set; }
    public IList<string> UnrunedCombine { get; } = new List<string>();

    public void SetContext(ISetContextCommand<char> command)
    {
        Alphabet = command.Alphabet;
        ArenaTreeContext = command.ArenaTreeContext;
        UnrunedCombine.Clear();
    }
}