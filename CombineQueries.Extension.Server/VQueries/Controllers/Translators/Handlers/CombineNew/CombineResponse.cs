using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translators.Handlers.CombineNew;

public record CombineResponse
{
    [JsonProperty("received")] public int Received { get; set; }
}
