using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Services.AFST;

public record MergeIterationResult(bool NeedsMore, int Depth);

// Итог приёма куска: собралось ли сообщение целиком и что именно собралось
public record ChunkResult(bool Complete, int Received, int Expected, string? Text);

public interface IAFST
{
    string? Alphabet { get; }

    // Выводится из Alphabet детерминированно (минус # % [ ]) - клиенту не передаётся
    string? WireAlphabet { get; }

    IArenaTreeRunes<char>? ArenaTreeContext { get; }
    IList<string> UnrunedCombine { get; }
    IList<string> CombineRunes { get; }

    // договор с клиентом из /init: сколько исходных символов несёт один кусок
    int RuneSize { get; }

    void SetContext(ISetContextCommand<char> command);

    // --- сборка сообщения ---

    // /n: объявлено K кусков и сколько символов срезать с конца (добивка)
    void Expect(int chunkCount, int pad);

    // /m: принять кусок. Когда набрано K - вернёт Complete со склеенным текстом
    ChunkResult Accept(string wireChunk);

    // --- интернирование ---

    // выдать (или переиспользовать) короткий идентификатор для собранной ссылки
    int Intern(string url);

    // вернёт null, если хэндл неизвестен (например, сервер рестартовал)
    string? Resolve(int handle);
}
