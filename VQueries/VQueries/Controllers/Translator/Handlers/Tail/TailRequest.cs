using MediatR;
using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Tail;

public record TailRequest : IRequest<TailResponse>
{
    [JsonProperty("runes")] public required string Runes { get; set; }

    [JsonProperty("direct")] public bool Direct { get; set; }
}
