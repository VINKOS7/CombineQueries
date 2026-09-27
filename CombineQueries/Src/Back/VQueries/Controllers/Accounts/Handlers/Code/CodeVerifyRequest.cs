using MediatR;

namespace CombineQueries.Api.Controllers.Accounts.Handlers.Code;

public record CodeVerifyRequest : IRequest<CodeVerifyResponse>;
