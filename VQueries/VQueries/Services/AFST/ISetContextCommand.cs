using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Services.AFST;

public interface ISetContextCommand<TRunes>
{
    string Alphabet { get; init; }
    IArenaTreeRunes<TRunes> ArenaTreeContext { get; init; }
}

public record SetContextCommand<TRunes>() : ISetContextCommand<TRunes>
{
    public IArenaTreeRunes<TRunes> ArenaTreeContext { get; init; }
    public string Alphabet { get; init; }
}