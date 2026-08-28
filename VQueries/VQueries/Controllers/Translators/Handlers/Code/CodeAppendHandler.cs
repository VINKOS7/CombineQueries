using MediatR;

using CombineQueries.Api.Services.Speech;
using CombineQueries.Api.Services.Auth;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Code;

public class CodeAppendHandler(IHttpContextAccessor http, ISpeech speech) : IRequestHandler<CodeAppendRequest, CodeAppendResponse>
{
    public Task<CodeAppendResponse> Handle(CodeAppendRequest request, CancellationToken cancellationToken)
    {
        string key = http.HttpContext is null ? "" : MasterGate.KeyOf(http.HttpContext);

        string segment = Clean(request.Segment);

        speech.AuthAppend(key, segment);

        return Task.FromResult(new CodeAppendResponse { Length = segment.Length });
    }

    private static string Clean(string segment)
    {
        foreach (char c in segment) if (!char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c)) return "";

        return segment.Length > 16 ? "" : segment;
    }
}
