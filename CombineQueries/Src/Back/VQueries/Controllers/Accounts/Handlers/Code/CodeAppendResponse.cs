using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Accounts.Handlers.Code;

public record CodeAppendResponse
{
    [JsonProperty("length")] public int Length { get; set; }
}
