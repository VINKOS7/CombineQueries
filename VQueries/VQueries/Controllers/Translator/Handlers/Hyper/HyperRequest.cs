using MediatR;
using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translator.Handlers.Hyper;

public record HyperRequest : IRequest<HyperResponse>
{
    [JsonProperty("handle")] public int Value { get; set; }

    public static HyperRequest From(string? query) => new() { Value = RawQuery.Int(query) };
}
