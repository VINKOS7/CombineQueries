using Dotseed.Domain;

namespace CombineQueries.Domain.Aggregates.Account;

public class Account : Entity, IAggregateRoot
{
    public new Guid Id { get; set; } = Guid.NewGuid();
    public required string Token { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool Active { get; set; } = true;

    public const int TokenMin = 8;
    public const int TokenMax = 64;

    public static Account From(IAddAccount command) => new()
    {
        Token = command.Token,

        Name = command.Name ?? string.Empty,
        Description = command.Description ?? string.Empty
    };

    public static bool IsToken(string? token)
    {
        if (string.IsNullOrEmpty(token)) return false;

        if (token.Length < TokenMin || token.Length > TokenMax) return false;

        foreach (char c in token) if (!char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_') return false;

        return true;
    }

    // Первое подключение в сессии: аккаунт заявляет свой алфавит и базовый адрес. Dotseed диспатчит
    // событие на SaveEntitiesAsync -> ConnectedHandler обеспечивает Translator.
    //
    // Событие ВОЗВРАЩАЕМ: обработчик уведомления результата не отдаёт, поэтому он кладёт добытый
    // агрегат в само событие, а вызвавший забирает его отсюда после сохранения.
    public AccountConnected Init(string alphabet, string baseForwardUrl) => Raise(alphabet, baseForwardUrl);

    // Повторное подключение тем же контекстом: мир уже знает алфавит, ему нужен лишь тёплый словарь
    // заново (после гашения хоста или реконнекта). Событие то же - Translator ищется по алфавиту и
    // переиспользуется, а не заводится второй.
    public AccountConnected Remember(string alphabet, string baseForwardUrl) => Raise(alphabet, baseForwardUrl);

    private AccountConnected Raise(string alphabet, string baseForwardUrl)
    {
        var connected = new AccountConnected(alphabet, baseForwardUrl);

        AddDomainEvent(connected);

        return connected;
    }
}
