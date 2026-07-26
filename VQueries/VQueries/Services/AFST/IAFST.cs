using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Services.AFST;

// Итог приёма куска: собралось ли сообщение целиком и что именно собралось.
// ElapsedMs - от /n до последнего куска, то есть вся сборка целиком. Ради этого числа
// всё и затевалось: с ним сравнивается время повторной отправки через /h.
public record RuneResult(bool Complete, int Received, int Expected, string? Text, long ElapsedMs);

public interface IAFST
{
    string? Alphabet { get; }

    // Выводится из Alphabet детерминированно (минус # % [ ]) - клиенту не передаётся
    string? WireAlphabet { get; }

    IArenaTreeRunes<char>? ArenaTreeContext { get; }
    IList<string> CombineRunes { get; }

    // договор с клиентом из /init: сколько исходных символов несёт один кусок
    int RuneSize { get; }

    void SetContext(ISetContextCommand<char> command);

    // --- сборка сообщения ---

    // /n: объявлено K кусков и сколько символов срезать с конца (добивка). Здесь же
    // стартует секундомер сборки - это первый запрос отправки, от него и считаем.
    void Expect(int runeCount, int pad);

    // /m: принять кусок. Когда набрано K - вернёт Complete со склеенным текстом
    RuneResult Accept(string wireRune);

    // --- интернирование ---

    // Выдать (или переиспользовать) короткий идентификатор для собранной ссылки.
    // firstSendMs запоминается ТОЛЬКО при первой выдаче: это эталон, с которым потом
    // сравнивается быстрый путь. Повторный Intern той же ссылки его не перетирает.
    int Intern(string url, long firstSendMs);

    // вернёт null, если хэндл неизвестен (например, сервер рестартовал)
    string? Resolve(int handle);

    // сколько заняла ПЕРВАЯ, полная отправка этой ссылки; -1 если хэндл неизвестен
    long FirstSendMsOf(int handle);
}
