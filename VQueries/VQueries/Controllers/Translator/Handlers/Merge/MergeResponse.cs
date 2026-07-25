using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translator.Handlers.Merge;

public record MergeResponse
{
    // сколько кусков уже склеено в буфере - ack клиенту, что мердж принят
    [JsonProperty("count")] public int Count { get; set; }
}
