using CombineQueries.Api.Services.Speech;
using MediatR;
using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Combine;

// Ответ двух видов: кусок (CombineResponse) или закрытие, если первая /c несёт весь адрес (TailResponse).
public record CombineRequest : IRequest<object>
{
    [JsonProperty("runes")] public required string Runes { get; set; }

    [JsonProperty("id")] public int Id { get; set; }

    [JsonProperty("page")] public int Page { get; set; }

    [JsonProperty("hop")] public int Hop { get; set; }

    [JsonProperty("q")] public int Q { get; set; }

    // Подпись: её несут VF и hop, по ней кусок ложится в сборку своего потока. -1 - руна, без подписи.
    [JsonProperty("sign")] public int Sign { get; set; } = -1;
}
