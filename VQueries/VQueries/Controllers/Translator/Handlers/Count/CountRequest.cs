using MediatR;
using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translator.Handlers.Count;

// Заголовок сообщения: сколько кусков будет и сколько символов срезать с хвоста.
// Оба числа приезжают одним значением: value = K * runeSize + pad.
public record CountRequest : IRequest<CountResponse>
{
    [JsonProperty("value")] public int Value { get; set; }
}
