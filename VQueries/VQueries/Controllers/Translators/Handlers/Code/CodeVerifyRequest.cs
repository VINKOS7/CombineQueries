using MediatR;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Code;

public record CodeVerifyRequest : IRequest<CodeVerifyResponse>;
