using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Page;

public record PageResponse
{
    [JsonProperty("received")] public int Received { get; set; }
}
