using MediatR;
using CombineQueries.Api.Services.AFST;
using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Tail;

public record TailRequest : IRequest<TailResponse>
{
    [JsonProperty("runes")] public required string Runes { get; set; }

    [JsonProperty("type")] public TypeCombine Type { get; set; }
}
