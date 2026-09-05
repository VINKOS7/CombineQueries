using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Services.Speech;

public record AssembledResult(string Text, int Runes, long ElapsedMs);

// Сид для connect и пиггибэк новых фрагментов в ответе /t/. Сериализуются camelCase:
// HyperSeed -> {handle,url}, FragmentSeed -> {id,text}.
public record HyperSeed(int Handle, string Url);

public record FragmentSeed(int Id, string Text);

public interface ISpeech
{
    string? Alphabet { get; }

    string? RuneAlphabet { get; }

    int RuneSize { get; }

    string Scheme { get; }

    int DfaSize { get; }

    string DirectRunes { get; }

    string DirectUnruned { get; }

    bool Authorized { get; }

    void Authorize();

    void AuthAppend(string segment);

    string AuthConsume();

    void SetContext(ISetContextCommand<char> command);

    int Accept(string rune);

    int AcceptVirtualFragment(int id);

    int SymbolsOf(TypeQuery type);

    AssembledResult Close(string tailText, TypeQuery type);

    int Intern(string url, long firstSendMs);

    string? Resolve(int handle);

    long FirstSendMsOf(int handle);

    string? ResolveVirtualFragment(int id);

    IReadOnlyList<string> HyperUrls { get; }

    IReadOnlyList<string> FragmentTexts { get; }

    IReadOnlyList<FragmentSeed> LearnFrom(string text);

    void PushDirectRunes(string runes);

    void PushDirect(string runes);

    void Foget();
}
