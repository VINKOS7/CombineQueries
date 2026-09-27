using MediatR;
using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Accounts.Handlers.List;

public record ListRequest : IRequest<ListResponse>, IMasterOnly
{
    [JsonProperty("master")] public string? Master { get; set; }
}
