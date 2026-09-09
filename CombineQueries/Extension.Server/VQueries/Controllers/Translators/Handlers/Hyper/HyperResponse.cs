using Newtonsoft.Json;

using CombineQueries.Api.Services.Outbox;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Hyper;

// Один адрес, за которым пошёл прыжок, и его номер.
public record SentUrl(
    [property: JsonProperty("url")] string Url,
    [property: JsonProperty("jump")] int Jump);

public record HyperResponse
{
    [JsonProperty("resumed")] public int Resumed { get; set; }

    [JsonProperty("known")] public bool Known { get; set; }

    // Что делать клиенту, если прыжок не сработал. Не ошибка: адрес просто собирается заново и
    // на хвосте снова попадает в индекс.
    [JsonProperty("note")] public string? Note { get; set; }

    // За какими адресами этот прыжок пошёл наружу и под какими номерами они лежат. Тел здесь нет
    // и быть не может: форвард уехал в фон, ответы приедут долгом - этим же ответом, если успели,
    // иначе следующим. Клиенту список нужен дважды: сказать в лог, за чем пошли, и запомнить
    // номера соседей - диапазон тащит их даром, а знать их иначе неоткуда.
    [JsonProperty("sent")] public IReadOnlyList<SentUrl>? Sent { get; set; }

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
