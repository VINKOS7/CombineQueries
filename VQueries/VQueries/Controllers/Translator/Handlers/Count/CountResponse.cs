using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translator.Handlers.Count;

public record CountResponse
{
    [JsonProperty("expected")] public int Expected { get; set; }
    [JsonProperty("pad")] public int Pad { get; set; }
}
