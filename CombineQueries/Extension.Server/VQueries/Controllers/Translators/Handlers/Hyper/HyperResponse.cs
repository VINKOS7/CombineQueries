using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Hyper;

public record HyperResponse
{
    [JsonProperty("known")] public bool Known { get; set; }

    [JsonProperty("forwardedUrl")] public string? ForwardedUrl { get; set; }
    [JsonProperty("response")] public string? Response { get; set; }

    [JsonProperty("elapsedMs")] public long ElapsedMs { get; set; }
    [JsonProperty("firstSendMs")] public long FirstSendMs { get; set; }
}
