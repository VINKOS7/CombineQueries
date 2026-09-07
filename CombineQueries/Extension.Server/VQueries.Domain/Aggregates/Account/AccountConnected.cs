using MediatR;

using CombineQueries.Domain.Aggregates.Translator;

namespace CombineQueries.Domain.Aggregates.Account;

// Кросс-агрегатная связь: аккаунт подключился - кто-то должен обеспечить Translator под его алфавит.
//
// INotificationHandler возвращает Task, то есть отдать результат обработчик не может. Поэтому
// событие работает КОНВЕРТОМ в обе стороны: обработчик инкапсулирует работу с репозиторием и
// кладёт добытый агрегат сюда, а поднявший событие забирает его после SaveEntitiesAsync.
public record AccountConnected(string Alphabet, string BaseForwardUrl) : INotification
{
    // null - персист недоступен (нет БД, конфиг-фолбэк). Протокол в этом случае живёт в памяти.
    public Translator.Translator? Translator { get; set; }
}
