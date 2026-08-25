using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Merge;

public record MergeResponse
{
    [JsonProperty("received")] public int Received { get; set; }
}
