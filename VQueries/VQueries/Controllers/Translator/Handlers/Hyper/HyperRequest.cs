using MediatR;
using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translator.Handlers.Hyper;

// Отправка уже известной серверу ссылки одним запросом.
// Value - хэндл, выданный в ответе /m при сборке этой же ссылки.
public record HyperRequest : IRequest<HyperResponse>
{
    [JsonProperty("handle")] public int Value { get; set; }
}
