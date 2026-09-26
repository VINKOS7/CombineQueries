using Newtonsoft.Json;

using CombineQueries.Api.Services.Outbox;
using CombineQueries.Api.Controllers.Accounts;


namespace CombineQueries.Api.Controllers.Translators.Handlers.Head;

public record Found(
    [property: JsonProperty("url")] string Url,
    [property: JsonProperty("jump")] int Jump
);

public record HeadResponse : ISigned
{
    [JsonProperty("signs")] public string? Signs { get; set; }

    [JsonProperty("known")] public bool Known { get; set; }

    [JsonProperty("found")] public IReadOnlyList<Found>? Found { get; set; }

    [JsonProperty("urls")] public int Urls { get; set; }

    [JsonProperty("kept")] public string? Kept { get; set; }

    [JsonProperty("ready")] public IReadOnlyList<Delivery>? Ready { get; set; }

    [JsonProperty("pending")] public int Pending { get; set; }

    [JsonProperty("elapsedMs")] public long ElapsedMs { get; set; }
}
