using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Tail;

public record TailResponse
{
    [JsonProperty("runes")] public int Runes { get; set; }
    [JsonProperty("forwardedUrl")] public string? ForwardedUrl { get; set; }
    [JsonProperty("response")] public string? Response { get; set; }
    [JsonProperty("handle")] public int Handle { get; set; } = -1;
    [JsonProperty("assemblyMs")] public long AssemblyMs { get; set; }
    [JsonProperty("forwardMs")] public long ForwardMs { get; set; }
}
