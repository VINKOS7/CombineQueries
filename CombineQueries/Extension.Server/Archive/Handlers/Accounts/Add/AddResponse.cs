using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Accounts.Handlers.Add;

public record AddResponse
{
    [JsonProperty("id")] public Guid Id { get; set; }

    // Единственный раз, когда токен уходит наружу целиком: дальше он только сверяется.
    [JsonProperty("token")] public string? Token { get; set; }

    [JsonProperty("name")] public string? Name { get; set; }
}
