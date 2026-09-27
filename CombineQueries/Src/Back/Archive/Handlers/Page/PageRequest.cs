using MediatR;
using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Page;

public record PageRequest : IRequest<PageResponse>
{
    [JsonProperty("page")] public int Page { get; set; }
}
