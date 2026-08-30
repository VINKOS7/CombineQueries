using MediatR;

using CombineQueries.Api.Services.Speech;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Code;

public class CodeAppendHandler(ISpeech speech) : IRequestHandler<CodeAppendRequest, CodeAppendResponse>
{
    public Task<CodeAppendResponse> Handle(CodeAppendRequest request, CancellationToken cancellationToken)
    {
        string segment = Clean(request.Segment);

        speech.AuthAppend(segment);

        return Task.FromResult(new CodeAppendResponse { Length = segment.Length });
    }

    private static string Clean(string segment)
    {
        foreach (char c in segment) if (!char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c)) return "";

        return segment.Length > 16 ? "" : segment;
    }
}
