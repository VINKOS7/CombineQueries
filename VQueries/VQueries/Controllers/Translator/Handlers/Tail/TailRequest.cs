using MediatR;
using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Tail;

public record TailRequest : IRequest<TailResponse>
{
    [JsonProperty("runes")] public required string Runes { get; set; }

    public static TailRequest From(string? query) => new() { Runes = RawQuery.Arg(query) };
}
