using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Accounts.Handlers.List;

// Токен наружу НЕ отдаём: он уходит один раз при создании. Здесь только хвост, чтобы опознать
// строку глазами, не раскрывая ключ.
public record AccountView(
    [property: JsonProperty("id")] Guid Id,
    [property: JsonProperty("name")] string? Name,
    [property: JsonProperty("description")] string? Description,
    [property: JsonProperty("active")] bool Active,
    [property: JsonProperty("tokenTail")] string TokenTail);

public record ListResponse
{
    [JsonProperty("accounts")] public IReadOnlyList<AccountView>? Accounts { get; set; }
}
