using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.TranslatorController.Handlers.CQMergeSend;

public record MergeResponse
{
    [JsonProperty("response")] public string delta { get; set; }
}