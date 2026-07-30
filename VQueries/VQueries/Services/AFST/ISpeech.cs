using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Services.AFST;

public record AssembledResult(string Text, int Runes, long ElapsedMs);

public interface ISpeech
{
    string? Alphabet { get; }

    string? WireAlphabet { get; }

    int RuneSize { get; }

    string Scheme { get; }

    void SetContext(ISetContextCommand<char> command);

    int Accept(string wireRune);

    AssembledResult Close(string tailText);

    int Intern(string url, long firstSendMs);

    string? Resolve(int handle);

    long FirstSendMsOf(int handle);
}
