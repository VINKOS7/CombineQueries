using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Services.Speech;

public record AssembledResult(string Text, int Runes, long ElapsedMs);

// Сид для connect и пиггибэк новых фрагментов в ответе /t/. Сериализуются camelCase:
// HyperSeed -> {handle,url}, FragmentSeed -> {id,text}.
public record HyperSeed(int Handle, string Url);

public record FragmentSeed(int Id, string Text);

// Итог обучения. Addressable - строки, которым хватило финитных адресов (L2/L3): они и едут клиенту
// пиггибэком. Overflowed - те, кому адресов не хватило: в БД лягут с Level=Infinite, а клиенту НЕ
// уходят, поэтому он их не адресует и шлёт буквами (тот самый direct-фоллбэк из спеки).
public record LearnResult(IReadOnlyList<FragmentSeed> Addressable, IReadOnlyList<FragmentSeed> Overflowed);

public interface ISpeech
{
    string? Alphabet { get; }

    string? RuneAlphabet { get; }

    int RuneSize { get; }

    string Scheme { get; }

    int DfaSize { get; }

    int PageCount { get; }

    // Развязка-3: сколько ёмкостей клиент умеет перепрыгнуть. 1 = за L3 ничего не адресуется.
    int HopCount { get; }

    string DirectRunes { get; }

    string DirectUnruned { get; }

    bool Authorized { get; }

    // Поток сборки сорван неверным запросом: принимать дальше нельзя до повторного connect.
    bool Broken { get; }

    string LastFault { get; }

    // Последовательность подписей хвоста, выданная на connect. Клиент шлёт очередную в /t/.
    string Signs { get; }

    void Authorize();

    void Fault(string reason);

    bool CheckSign(int sign);

    void AuthAppend(string segment);

    string AuthConsume();

    void SetContext(ISetContextCommand<char> command);

    // Тёплый словарь из персиста в рантайм. Сиды упорядочены по id/handle - они же индексы.
    void Restore(IReadOnlyList<FragmentSeed> fragments, IReadOnlyList<HyperSeed> hypers);

    int Accept(string rune);

    void SetFragmentPage(int page);

    int AcceptVirtualFragment(int id);

    // Развязка-3: сдвигает последний принятый VF на hops ёмкостей (старший разряд адреса Infinite).
    int Hop(int hops);

    int SymbolsOf(TypeQuery type);

    AssembledResult Close(string tailText, TypeQuery type);

    int Intern(string url, long firstSendMs);

    string? Resolve(int handle);

    long FirstSendMsOf(int handle);

    string? ResolveVirtualFragment(int id);

    IReadOnlyList<string> HyperUrls { get; }

    IReadOnlyList<string> FragmentTexts { get; }

    LearnResult LearnFrom(string text);

    void PushDirectRunes(string runes);

    void PushDirect(string runes);

    void Foget();
}
