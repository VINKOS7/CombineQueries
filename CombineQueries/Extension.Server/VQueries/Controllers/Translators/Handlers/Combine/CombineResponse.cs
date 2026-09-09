using Newtonsoft.Json;

using CombineQueries.Api.Services.Outbox;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Combine;

public record CombineResponse
{
    // Долг: тела, доспевшие к этому мгновению. Комбайн наружу не ходит, но довесок цепляется к
    // ЛЮБОМУ ответу - иначе то, что доспело между запросами, ждало бы конца сборки.
    [JsonProperty("ready")] public IReadOnlyList<Delivery>? Ready { get; set; }

    [JsonProperty("pending")] public int Pending { get; set; }

    [JsonProperty("received")] public int Received { get; set; }
}
