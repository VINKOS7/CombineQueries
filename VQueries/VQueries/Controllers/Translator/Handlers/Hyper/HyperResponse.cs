using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translator.Handlers.Hyper;

public record HyperResponse
{
    // false = сервер такого хэндла не знает (рестартовал). Клиент обязан вычистить кэш
    // и отправить ссылку полностью, иначе будет слать в пустоту после каждого рестарта.
    [JsonProperty("known")] public bool Known { get; set; }

    [JsonProperty("forwardedUrl")] public string? ForwardedUrl { get; set; }
    [JsonProperty("response")] public string? Response { get; set; }

    // Сколько заняла ЭТА отправка и сколько занимала ПЕРВАЯ (полная, через /n + куски).
    // Обе меряются на сервере, поэтому не включают время самих round-trip'ов до Udon -
    // реальный выигрыш у клиента будет больше, здесь видна только серверная часть.
    [JsonProperty("elapsedMs")] public long ElapsedMs { get; set; }
    [JsonProperty("firstSendMs")] public long FirstSendMs { get; set; }
}
