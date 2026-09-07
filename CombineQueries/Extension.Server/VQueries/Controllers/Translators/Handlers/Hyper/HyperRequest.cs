using MediatR;
using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Hyper;

public record HyperRequest : IRequest<HyperResponse>
{
    [JsonProperty("handle")] public int Value { get; set; }

    // Очередная подпись из общего с хвостом кольца, но одним битом - см. Speech.CheckSign.
    [JsonProperty("sign")] public int Sign { get; set; }
}
