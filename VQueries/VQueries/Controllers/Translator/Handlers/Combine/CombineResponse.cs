using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.TranslatorController.Handlers.CQMergeSend;

public record CombineResponse
{
    [JsonProperty("response")] public string? Response { get; set; }
}