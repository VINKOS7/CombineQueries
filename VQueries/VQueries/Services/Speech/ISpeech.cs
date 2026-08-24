using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Services.Speech;

public record AssembledResult(string Text, int Runes, long ElapsedMs);

public interface ISpeech
{
    string? Alphabet { get; }

    string? RuneAlphabet { get; }

    int RuneSize { get; }

    string Scheme { get; }

    string DirectRunes { get; }

    string DirectUnruned { get; }

    void SetContext(ISetContextCommand<char> command);

    int Accept(string rune);

    int SymbolsOf(TypeCombine type);

    AssembledResult Close(string tailText, TypeCombine type);

    int Intern(string url, long firstSendMs);

    string? Resolve(int handle);

    long FirstSendMsOf(int handle);

    void PushDirectRunes(string runes);

    void PushDirect(string runes);

    void Foget();
}
