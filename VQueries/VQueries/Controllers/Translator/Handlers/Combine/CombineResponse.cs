using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translator.Handlers.Combine;

public record CombineResponse
{
    // сколько кусков принято из скольки ожидаемых - по этим числам клиент видит потерю
    [JsonProperty("received")] public int Received { get; set; }
    [JsonProperty("expected")] public int Expected { get; set; }

    // ниже заполняется только когда сообщение собралось целиком
    [JsonProperty("complete")] public bool Complete { get; set; }
    [JsonProperty("forwardedUrl")] public string? ForwardedUrl { get; set; }
    [JsonProperty("response")] public string? Response { get; set; }

    // короткий идентификатор собранной ссылки: в следующий раз её можно послать ОДНИМ запросом
    [JsonProperty("handle")] public int Handle { get; set; } = -1;
}
