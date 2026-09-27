using MediatR;

namespace CombineQueries.Api.Controllers.Accounts.Handlers.Code;

public record CodeAppendRequest : IRequest<CodeAppendResponse>
{
    public required string Segment { get; set; }
}
