using CombineQueries.Api.Services.Speech;
using MediatR;
using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Combine;

public record MergeRequest : IRequest<MergeResponse>
{
    [JsonProperty("runes")] public required string Runes { get; set; }
}
