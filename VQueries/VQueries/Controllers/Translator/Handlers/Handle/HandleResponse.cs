using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translator.Handlers.Handle;

public record HandleResponse
{
    // false = сервер такого хэндла не знает (рестартовал). Клиент обязан вычистить кэш
    // и отправить ссылку полностью, иначе будет слать в пустоту после каждого рестарта.
    [JsonProperty("known")] public bool Known { get; set; }

    [JsonProperty("forwardedUrl")] public string? ForwardedUrl { get; set; }
    [JsonProperty("response")] public string? Response { get; set; }
}
