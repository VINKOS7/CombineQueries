using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Services.AFST;

public record AssembledResult(string Text, int Runes, long ElapsedMs);

public interface ISpeach
{
    string? Alphabet { get; }

    string? RuneAlphabet { get; }

    int RuneSize { get; }

    string Scheme { get; }

    void SetContext(ISetContextCommand<char> command);

    int Accept(string rune);

    int SymbolsOf(TypeRune type);

    AssembledResult Close(string tailText, TypeRune type);

    int Intern(string url, long firstSendMs);

    string? Resolve(int handle);

    long FirstSendMsOf(int handle);
}
