using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translator.Handlers.Tail;

public record TailResponse
{
    [JsonProperty("expected")] public int Expected { get; set; }
    [JsonProperty("pad")] public int Pad { get; set; }
}
