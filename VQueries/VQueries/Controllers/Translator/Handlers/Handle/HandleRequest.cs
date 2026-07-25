using MediatR;
using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translator.Handlers.Handle;

// Отправка уже известной серверу ссылки одним запросом
public record HandleRequest : IRequest<HandleResponse>
{
    [JsonProperty("handle")] public int Value { get; set; }
}
