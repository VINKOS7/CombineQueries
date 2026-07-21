using CombineQueries.Domain.Aggregates.Translator.types;
namespace CombineQueries.Api.Services.AFST;

public interface IAFST
{
    string? Alphabet { get; }
    IArenaTreeRunes<char>? ArenaTreeContext { get; }
    IList<string> UnrunedCombine { get; }
    void SetContext(ISetContextCommand<char> command);
}