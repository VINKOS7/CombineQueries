using Newtonsoft.Json;

using CombineQueries.Api.Services.Outbox;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Credit;

public record CreditResponse
{
    // Доспевшее к этому мгновению. Формат тот же, что у довеска к любому другому ответу: клиент
    // разбирает долг одним и тем же кодом, каким бы эндпоинтом он ни приехал.
    [JsonProperty("ready")] public IReadOnlyList<Delivery>? Ready { get; set; }

    // Сколько ещё в полёте. Больше нуля - клиенту есть смысл прийти за остатком снова.
    [JsonProperty("pending")] public int Pending { get; set; }
}
