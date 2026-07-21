using MediatR;
using Newtonsoft.Json;
using CombineQueries.Domain.Aggregates.Translator;
using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Controllers.TranslatorController.Handlers.Init;

public record InitRequest : IRequest<InitResponse>
{
    [JsonProperty("alphabet")] public required string Alphabet { get; set; }
    [JsonProperty("baseQuery")] public required string baseForwardUrl { get; set; }

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