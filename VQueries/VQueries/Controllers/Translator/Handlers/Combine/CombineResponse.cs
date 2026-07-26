using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translator.Handlers.Combine;

public record CombineResponse
{
    [JsonProperty("received")] public int Received { get; set; }
}
