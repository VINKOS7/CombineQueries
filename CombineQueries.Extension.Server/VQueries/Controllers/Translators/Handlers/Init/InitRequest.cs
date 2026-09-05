using MediatR;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Mvc;
using CombineQueries.Domain.Aggregates.Translator;
using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Init;

public record InitRequest : IRequest<InitResponse>
{
    [JsonProperty("alphabet")] public required string Alphabet { get; set; }

    [JsonProperty("baseQuery")]
    [FromQuery(Name = "baseQuery")]
    public required string baseForwardUrl { get; set; }

    [JsonProperty("runeSize")] public int RuneSize { get; set; } = 2;

    [JsonProperty("scheme")] public string Scheme { get; set; } = "https";

    [JsonProperty("token")] public string Token { get; set; } = "";

    // Размер адресной цепи фрагментов: сколько /f/-слотов клиент запёк. Сервер не выдаёт
    // id >= dfaSize (клиент такой не адресует). 0 = клиент фрагменты не поддерживает.
    [JsonProperty("dfaSize")] public int DfaSize { get; set; } = 0;

    // Число страниц Развязки-2 (/g/): L3-ёмкость = dfaSize*pageCount. 1 = только L2.
    [JsonProperty("pageCount")] public int PageCount { get; set; } = 1;
}

public record InitCommand<TSymbol> : IAddTranslator<TSymbol> where TSymbol : notnull
{
    public required string Alphabet { get; set; }
    public required string BaseForwardUrl { get; set; }
    public required IArenaTreeRunes<TSymbol> Runes { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int SizeRune { get; set; }
}
