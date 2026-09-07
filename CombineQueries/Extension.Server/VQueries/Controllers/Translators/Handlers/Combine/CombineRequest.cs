using CombineQueries.Api.Services.Speech;
using MediatR;
using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Combine;

public record CombineRequest : IRequest<CombineResponse>
{
    [JsonProperty("runes")] public required string Runes { get; set; }
    [JsonProperty("id")] public int Id { get; set; }
    [JsonProperty("page")] public int Page { get; set; }

    // Развязка-3 (Infinite): на сколько ёмкостей сдвинуть последний принятый VF. 0 = обычный запрос.
    [JsonProperty("hop")] public int Hop { get; set; }

    [JsonProperty("q")] public int Q { get; set; }
}
