using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translator.Handlers.Init;

public record InitResponse
{
    [JsonProperty("shortDomain")] required public string? ShortDomain { get; set; }

    [JsonProperty("runeSize")] public int RuneSize { get; set; }
}
