using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Code;

public record CodeAppendResponse
{
    [JsonProperty("length")] public int Length { get; set; }
}
