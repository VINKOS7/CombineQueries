using MediatR;
using Newtonsoft.Json;

using CombineQueries.Domain.Aggregates.Account;
using CombineQueries.Api.Controllers.Accounts.Handlers;

namespace CombineQueries.Api.Controllers.Accounts.Handlers.Add;

public record AddRequest : IRequest<AddResponse>, IAddAccount, IMasterOnly
{
    [JsonProperty("master")] public string? Master { get; set; }

    // Пусто - сгенерируем сами: руками токены выходят короткие и предсказуемые.
    [JsonProperty("token")] public string Token { get; set; } = "";

    [JsonProperty("name")] public string? Name { get; set; }

    [JsonProperty("description")] public string? Description { get; set; }
}
