using MediatR;
using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Hyper;

public record HyperRequest : IRequest<HyperResponse>
{
    [JsonProperty("handle")] public int Value { get; set; }

}
