using MediatR;
using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Fragment;

public record FragmentRequest : IRequest<FragmentResponse>
{
    [JsonProperty("id")] public int Id { get; set; }
}
