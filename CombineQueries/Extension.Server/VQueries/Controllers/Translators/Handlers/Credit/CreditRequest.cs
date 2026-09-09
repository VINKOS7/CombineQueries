using MediatR;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Credit;

// Забрать долг, не прося ничего нового.
//
// Нужен ровно на один случай: клиенту слать больше нечего, а за сервером ещё числятся тела. Без
// такого запроса они висели бы до следующего Send, то есть неизвестно сколько.
public record CreditRequest : IRequest<CreditResponse>
{
    public int Sign { get; set; }
}
