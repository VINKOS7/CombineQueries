using Newtonsoft.Json;

using CombineQueries.Api.Services.Outbox;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Hyper;

// Один отданный адрес: что запросили наружу, что оттуда пришло и сколько это заняло.
public record HttpsRequest(
    [property: JsonProperty("url")] string Url,
    [property: JsonProperty("response")] string Response,
    [property: JsonProperty("elapsedMs")] long ElapsedMs);

public record HyperResponse
{
    [JsonProperty("resumed")] public int Resumed { get; set; }

    [JsonProperty("known")] public bool Known { get; set; }

    // Что делать клиенту, если прыжок не сработал. Не ошибка: адрес просто собирается заново и
    // на хвосте снова попадает в индекс.
    [JsonProperty("note")] public string? Note { get; set; }

    // Что прыжок отдал наружу. МАССИВ, хотя сейчас в нём всегда один адрес: прыжок по смыслу
    // возвращает столько запросов, сколько покрыл, и клиент должен уметь читать их пачкой -
    // иначе переход на несколько адресов сломает фронт задним числом.
    [JsonProperty("httpsRequests")] public IReadOnlyList<HttpsRequest>? HttpsRequests { get; set; }

    // Сколько адресов в массиве. Отдельным числом, чтобы клиенту не считать длину ради лога.
    [JsonProperty("urls")] public int Urls { get; set; }

    // Первый адрес массива - для логов и совместимости с хвостом, тело сюда НЕ дублируем.
    [JsonProperty("forwardedUrl")] public string? ForwardedUrl { get; set; }

    // Доспевшее к этому моменту - неважно, по чьей просьбе за ним ходили. Клиент читает довесок
    // ПЕРВЫМ, а уже потом своё: тело его собственного адреса может оказаться как раз здесь.
    [JsonProperty("ready")] public IReadOnlyList<Delivery>? Ready { get; set; }

    // Сколько адресов ещё в полёте: значит будет и следующий довесок.
    [JsonProperty("pending")] public int Pending { get; set; }

    [JsonProperty("elapsedMs")] public long ElapsedMs { get; set; }

    [JsonProperty("firstSendMs")] public long FirstSendMs { get; set; }
}
