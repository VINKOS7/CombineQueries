using MediatR;
using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.TranslatorController.Handlers.CQMergeSend;

public record MergeRequest : IRequest<MergeResponse>
{
    [JsonProperty("runes")] public string Runes { get; set; }
}
