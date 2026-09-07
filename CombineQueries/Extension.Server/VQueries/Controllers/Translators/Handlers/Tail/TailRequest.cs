using MediatR;

using Newtonsoft.Json;

using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Tail;

public record TailRequest : IRequest<TailResponse>
{
    [JsonProperty("runes")] public required string Runes { get; set; }

    [JsonProperty("type")] public TypeQuery Type { get; set; }

    // Очередная подпись из выданной на connect последовательности.
    [JsonProperty("sign")] public int Sign { get; set; }
}
