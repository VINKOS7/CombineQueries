using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Services.Speech;

public interface ISetContextCommand<TRunes>
{
    string Alphabet { get; init; }
    int RuneSize { get; init; }
    string Scheme { get; init; }
}

public record SetContextCommand<TRunes>() : ISetContextCommand<TRunes>
{
    public required string Alphabet { get; init; }
    public int RuneSize { get; init; } = 2;
    public string Scheme { get; init; } = "https";
}
