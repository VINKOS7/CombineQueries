using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translator.Handlers.MergeSend;

public record MergeSendResponse
{
    // собранный из всех мерджей исходный URL - что реально форвардили
    [JsonProperty("forwardedUrl")] public string? ForwardedUrl { get; set; }

    // тело ответа целевого ресурса
    [JsonProperty("response")] public string? Response { get; set; }
}
