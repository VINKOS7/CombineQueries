using CombineQueries.Api.Services.Speech;
using MediatR;
using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translators.Handlers.CombineNew;

public record CombineRequest : IRequest<CombineResponse>
{
    [JsonProperty("runes")] public required string Runes { get; set; }
    [JsonProperty("id")] public int Id { get; set; }
}
