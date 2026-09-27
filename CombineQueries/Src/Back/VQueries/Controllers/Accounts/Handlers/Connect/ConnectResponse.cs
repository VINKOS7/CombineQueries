using Newtonsoft.Json;

using CombineQueries.Api.Services.Speech;

namespace CombineQueries.Api.Controllers.Accounts.Handlers.Connect;

public record ConnectResponse
{
    [JsonProperty("shortDomain")] required public string? ShortDomain { get; set; }

    [JsonProperty("runeSize")] public int RuneSize { get; set; }

    [JsonProperty("scheme")] public string? Scheme { get; set; }

    [JsonProperty("dfaSize")] public int DfaSize { get; set; }

    // connect-сид: тёплый словарь мастера целиком, чтобы клиент стартовал не с нуля.
    // Индекс в списке = handle/id (тот же, что зовёт /h/<handle> и /f/<id>).
    [JsonProperty("hypers")] public IReadOnlyList<HyperSeed>? Hypers { get; set; }

    [JsonProperty("fragments")] public IReadOnlyList<FragmentSeed>? Fragments { get; set; }

    // Последовательность подписей хвоста: клиент шлёт очередную в /t/, сервер сверяет.
    // Не совпало - приём валится до повторного connect.
    [JsonProperty("signs")] public string? Signs { get; set; }

    // Хайперы: адрес -> номер прыжка. Клиент держит их плоским словарём, дерево живёт на сервере.
    [JsonProperty("jumps")] public IReadOnlyList<JumpSeed>? Jumps { get; set; }

    // 35 корневых фрагментов-символов (L1). Клиент печёт только их КОЛИЧЕСТВО, а строки берёт
    // отсюда → удон перестаёт хардкодить словарь (спека: содержимое на беке).
    [JsonProperty("roots")] public IReadOnlyList<string>? Roots { get; set; }
}
