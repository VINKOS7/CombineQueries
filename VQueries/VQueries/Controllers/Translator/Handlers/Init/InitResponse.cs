using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.TranslatorController.Handlers.Init;

public record InitResponse
{
    [JsonProperty("shortDomain")] required public string? ShortDomain { get; set; }
}