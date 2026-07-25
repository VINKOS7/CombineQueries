using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translator.Handlers.Combine;

public record CombineResponse
{
    // merge: сколько кусков склеено в буфере (ack). mergesend: сколько было склеено перед отправкой
    [JsonProperty("count")] public int Count { get; set; }

    // ниже - только для mergesend, у merge остаются null
    [JsonProperty("forwardedUrl")] public string? ForwardedUrl { get; set; }
    [JsonProperty("response")] public string? Response { get; set; }
}
