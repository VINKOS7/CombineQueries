using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translator.Handlers.Init;

public record InitResponse
{
    [JsonProperty("shortDomain")] required public string? ShortDomain { get; set; }

    // эхо принятого договора - клиент должен сверить, что сервер согласился на ту же ширину руны
    [JsonProperty("runeSize")] public int RuneSize { get; set; }
}
