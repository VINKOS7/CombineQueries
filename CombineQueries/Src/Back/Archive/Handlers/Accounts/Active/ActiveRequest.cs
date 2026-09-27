using MediatR;
using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Accounts.Handlers.Active;

// Не удаление: аккаунт выключают, чтобы отозвать доступ, но сохранить след. Удаление увело бы
// вместе с собой и историю, а восстановить токен потом нечем.
public record ActiveRequest : IRequest<ActiveResponse>, IMasterOnly
{
    [JsonProperty("master")] public string? Master { get; set; }

    [JsonProperty("id")] public Guid Id { get; set; }

    [JsonProperty("active")] public bool Active { get; set; }
}
