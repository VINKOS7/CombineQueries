using MediatR;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Code;

public record CodeAppendRequest : IRequest<CodeAppendResponse>
{
    public required string Segment { get; set; }
}
