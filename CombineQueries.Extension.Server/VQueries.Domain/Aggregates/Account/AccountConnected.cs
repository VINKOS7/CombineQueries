using MediatR;

namespace CombineQueries.Domain.Aggregates.Account;

// Доменное событие: мастер прошёл авторизацию и подключился. Поднимается на Account,
// Dotseed диспатчит его на SaveEntitiesAsync -> ConnectedHandler обеспечивает Translator.
// Так связь Account -> Translator идёт через событие (правило «один хендлер — один репо»).
public record AccountConnected(string Alphabet, string BaseForwardUrl) : INotification;
