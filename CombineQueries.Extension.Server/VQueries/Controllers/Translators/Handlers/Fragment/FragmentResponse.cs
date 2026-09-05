using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Fragment;

public record FragmentResponse
{
    [JsonProperty("received")] public int Received { get; set; }
}
