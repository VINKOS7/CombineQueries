using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Services.Speech;

public interface ISetContextCommand<TRunes>
{
    string Alphabet { get; init; }
    int RuneSize { get; init; }
    string Scheme { get; init; }
    int DfaSize { get; init; }
    int PageCount { get; init; }

    // Развязка-3: сколько ёмкостей клиент умеет перепрыгнуть. 1 = развязки нет, только L2+L3.
    int HopCount { get; init; }

    // Класть ли НОВЫЕ цепочки в персист. false - дерево читается, но не растёт в БД.
    bool Hypers { get; init; }

    // Куда транслятор форвардит, если его придётся заводить впервые.
    string BaseForwardUrl { get; init; }

    // Забыть накопленные хайперы при подключении. Уважается только в Development.
    bool ResetHypers { get; init; }
}

public record SetContextCommand<TRunes>() : ISetContextCommand<TRunes>
{
    public required string Alphabet { get; init; }
    public int RuneSize { get; init; } = 2;
    public string Scheme { get; init; } = "https";
    public int DfaSize { get; init; }
    public int PageCount { get; init; } = 1;
    public int HopCount { get; init; } = 1;
    public bool Hypers { get; init; } = true;
    public string BaseForwardUrl { get; init; } = "";
    public bool ResetHypers { get; init; }
}
