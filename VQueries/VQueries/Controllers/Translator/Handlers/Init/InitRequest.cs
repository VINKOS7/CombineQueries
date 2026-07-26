using MediatR;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Mvc;
using CombineQueries.Domain.Aggregates.Translator;
using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Controllers.Translator.Handlers.Init;

public record InitRequest : IRequest<InitResponse>
{
    // Приезжает PERCENT-ENCODED: сырым нельзя, в алфавите есть # (начинает фрагмент)
    // и % (начинает escape-последовательность). Биндер декодирует сам.
    [JsonProperty("alphabet")] public required string Alphabet { get; set; }

    // FromQuery обязателен: JsonProperty биндингом query НЕ читается, а имя параметра у клиента
    // (baseQuery) с именем свойства (baseForwardUrl) не совпадает - без этого сюда приезжал null.
    [JsonProperty("baseQuery")]
    [FromQuery(Name = "baseQuery")]
    public required string baseForwardUrl { get; set; }

    // Сколько исходных символов едет в одном куске = ширина пула VRCUrl на клиенте.
    // На объём провода почти не влияет, влияет на число round-trip'ов (длина / RuneSize)
    // и КВАДРАТИЧНО на размер пула: 59^2 = 3 481, 59^3 = 205 379, 59^4 = 12 117 361.
    [JsonProperty("runeSize")] public int RuneSize { get; set; } = 2;

    [JsonProperty("name")] public string? Name { get; set; }
    [JsonProperty("description")] public string? Description { get; set; }
}

public record InitCommand<TSymbol> : IAddTranslator<TSymbol>
{
    public required string Alphabet { get; set; }
    public required string BaseForwardUrl { get; set; }
    public required IArenaTreeRunes<TSymbol> Runes { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
}