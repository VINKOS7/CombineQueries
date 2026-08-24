using MediatR;

using Newtonsoft.Json;

using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Tail;

public record TailRequest : IRequest<TailResponse>
{
    [JsonProperty("runes")] public required string Runes { get; set; }

    [JsonProperty("type")] public TypeCombine Type { get; set; }
}
