using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Services.Speech;

public interface ISetContextCommand<TRunes>
{
    string Alphabet { get; init; }
    int RuneSize { get; init; }
    string Scheme { get; init; }
    int DfaSize { get; init; }
    int PageCount { get; init; }
}

public record SetContextCommand<TRunes>() : ISetContextCommand<TRunes>
{
    public required string Alphabet { get; init; }
    public int RuneSize { get; init; } = 2;
    public string Scheme { get; init; } = "https";
    public int DfaSize { get; init; }
    public int PageCount { get; init; } = 1;
}
