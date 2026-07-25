using CombineQueries.Domain.Aggregates.Translator.types;
namespace CombineQueries.Api.Services.AFST;

public record MergeIterationResult(bool NeedsMore, int Depth);

public interface IAFST
{
    string? Alphabet { get; }
    IArenaTreeRunes<char>? ArenaTreeContext { get; }
    IList<string> UnrunedCombine { get; }
    IList<string> CombineRunes { get; }

    // договор с клиентом из /init: сколько рун несёт один запрос
    int RuneSize { get; }

    void SetContext(ISetContextCommand<char> command);
}