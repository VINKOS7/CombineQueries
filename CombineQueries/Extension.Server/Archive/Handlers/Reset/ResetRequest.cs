using MediatR;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Reset;

// Параметров нет: сброс касается всей сессии, выбирать тут нечего.
public record ResetRequest : IRequest<ResetResponse>;
