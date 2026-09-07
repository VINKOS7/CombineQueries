using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Accounts.Handlers.Active;

public record ActiveResponse
{
    [JsonProperty("id")] public Guid Id { get; set; }

    [JsonProperty("active")] public bool Active { get; set; }
}
