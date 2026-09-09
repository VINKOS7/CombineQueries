using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Services.Speech;

// Runes - сколько кусков всего. Дальше разбивка: сколько ушло рунами (не нашлось фрагмента) и
// сколько фрагментами по уровням. Это и отличает partial от полного покрытия.
public record AssembledResult(string Text, int Runes, long ElapsedMs, int Chunks, int L2, int L3, int Infinite);

// Сид для connect и пиггибэк новых фрагментов в ответе /t/. Сериализуются camelCase:
// HyperSeed -> {handle,url}, FragmentSeed -> {id,text}.
public record HyperSeed(int Handle, string Url);

public record FragmentSeed(int Id, string Text);

// Сид хайпера: собранный адрес и номер узла, которым он прыгается.
public record JumpSeed(string Url, int Jump);

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

    // Класть ли НОВЫЕ цепочки в персист: с hypers=off накопленное читается, но не пополняется.
    bool Hypers { get; }

    // Контекст подключения: обработчик доменного события берёт его отсюда, а не из аргументов.
    string BaseForwardUrl { get; }

    bool ResetHypers { get; }

    string DirectRunes { get; }

    string DirectUnruned { get; }

    bool Authorized { get; }

    // Поток сборки сорван неверным запросом: принимать дальше нельзя до повторного connect.
    bool Broken { get; }

    string LastFault { get; }

    // Последовательность подписей хвоста, выданная на connect. Клиент шлёт очередную в /t/.
    string Signs { get; }

    void Authorize();

    // Прогреть хайперы по текущему словарю. Зовётся на авторизации мастера, возвращает сколько
    // цепочек завёл.
    int Preheat();

    void Fault(string reason);

    bool CheckSign(int sign);

    // Подпись пониженного разрешения: то же кольцо, но сверяется остаток по values.
    bool CheckSign(int sign, int values);

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

    // Хайпер-дерево цепочек: растёт на каждой сборке, живёт в памяти.
    int TreeChains { get; }

    int TreeNodes { get; }

    int TreeDeepest { get; }

    int Ahead();

    IReadOnlyList<(int Id, int? ParentId, string Step, string? Url)> TakeChains();

    void RestoreChains(IEnumerable<(int Id, int? ParentId, string Step, string? Url)> nodes);

    IEnumerable<(string Url, int Jump)> ChainLeaves();

    // Точный поиск: база (по обрезку номера) плюс кусок расхождения.
    IEnumerable<(string Url, int Jump)> ChainsFrom(int shortened, string text, int limit);

    // Номера последней цепочки: лист и последний общий с известными узел.
    int LastLeaf { get; }

    int LastPrefix { get; }

    int LastShared { get; }

    // Встать в точку цепочки по её номеру. Возвращает, сколько кусков восстановлено, -1 - неизвестен.
    int Resume(int handle);

    string? UrlOf(int handle);

    // Придержать кусок промахнувшейся головы: он приклеится концом адреса при закрытии.
    void Keep(string text);

    // Узел и его соседи по родителю: адреса, отличающиеся от него ровно последним куском.
    IEnumerable<(string Url, int Jump)> Family(int handle, int limit);

    // Сброс хайперов: нужен, чтобы прогон теста был повторяемым.
    void ForgetHypers();

    long FirstSendMsOf(int handle);

    string? ResolveVirtualFragment(int id);

    IReadOnlyList<string> HyperUrls { get; }

    IReadOnlyList<string> FragmentTexts { get; }

    LearnResult LearnFrom(string text);

    void PushDirectRunes(string runes);

    void PushDirect(string runes);

    void Foget();
}
