using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.TranslatorController.Handlers.Send;

public record SendResponse
{
    [JsonProperty("response")] public string Response { get; set; }
}