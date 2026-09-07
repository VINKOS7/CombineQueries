using Newtonsoft.Json;

using CombineQueries.Api.Services.Speech;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Tail;

public record TailResponse
{
    [JsonProperty("runes")] public int Runes { get; set; }
    [JsonProperty("forwardedUrl")] public string? ForwardedUrl { get; set; }
    [JsonProperty("response")] public string? Response { get; set; }
    [JsonProperty("handle")] public int Handle { get; set; } = -1;
    [JsonProperty("assemblyMs")] public long AssemblyMs { get; set; }
    [JsonProperty("forwardMs")] public long ForwardMs { get; set; }

    // Новые адресуемые фрагменты, выученные из ЭТОГО URL. Клиент кладёт их в свой словарь
    // и со следующего раза зовёт /f/<id> вместо рун. Пусто, если ничего не доросло.
    [JsonProperty("fragments")] public IReadOnlyList<FragmentSeed>? Fragments { get; set; }
}
