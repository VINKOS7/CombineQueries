using MediatR;
using Newtonsoft.Json;
using CombineQueries.Domain.Aggregates.Translator;
using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Controllers.Translator.Handlers.Init;

public record InitRequest : IRequest<InitResponse>
{
    [JsonProperty("alphabet")] public required string Alphabet { get; set; }
    [JsonProperty("baseQuery")] public required string baseForwardUrl { get; set; }

    // Сколько рун в одном запросе = ширина пула VRCUrl на клиенте = сколько исходных символов
    // едет за раз. 2 = глубина рекурсии 1, 4 = глубина 2. На объём провода не влияет (это
    // тождество), влияет на число round-trip'ов и на потолок адресации узлов в tree-режиме.
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