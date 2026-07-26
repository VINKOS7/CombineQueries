using MediatR;
using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translator.Handlers.Combine;

public record CombineRequest : IRequest<CombineResponse>
{
    [JsonProperty("runes")] public required string Runes { get; set; }

    public static CombineRequest From(string? query) => new() { Runes = RawQuery.Arg(query) };
}
