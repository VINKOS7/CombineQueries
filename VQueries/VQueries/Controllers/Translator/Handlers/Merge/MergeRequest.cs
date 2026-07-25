using MediatR;
using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translator.Handlers.Merge;

public record MergeRequest : IRequest<MergeResponse>
{
    [JsonProperty("runes")] public string Runes { get; set; }
}
