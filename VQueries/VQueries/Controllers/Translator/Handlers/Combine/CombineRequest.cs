using MediatR;
using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translator.Handlers.Combine;

public record CombineRequest : IRequest<CombineResponse>
{
    [JsonProperty("runes")] public string Runes { get; set; }
}
