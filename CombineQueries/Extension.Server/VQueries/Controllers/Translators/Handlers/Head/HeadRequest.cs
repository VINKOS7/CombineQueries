using MediatR;
using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Head;

public record HeadRequest : IRequest<HeadResponse>
{
    [JsonProperty("fragment")] public int Fragment { get; set; }

    [JsonProperty("base")] public int Base { get; set; } = -1;

    [JsonProperty("sign")] public int Sign { get; set; }
}
