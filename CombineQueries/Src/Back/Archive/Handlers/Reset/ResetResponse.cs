using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Reset;

public record ResetResponse
{
    [JsonProperty("forgotten")] public int Forgotten { get; set; }
}
