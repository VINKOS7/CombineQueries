using MediatR;
using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.TranslatorController.Handlers.CQMergeSend;

public record CombineRequest : IRequest<CombineResponse>
{
    [JsonProperty("runes")] public string Runes { get; set; }
}
