using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Code;

public record CodeVerifyResponse
{
    [JsonProperty("bound")] public bool Bound { get; set; }
}
