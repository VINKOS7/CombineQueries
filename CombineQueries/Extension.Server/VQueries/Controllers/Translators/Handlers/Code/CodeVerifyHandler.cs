using MediatR;

using CombineQueries.Api.Services.Speech;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Code;

public class CodeVerifyHandler(IConfiguration configuration, ILogger<CodeVerifyHandler> logger, ISpeech speech)
    : IRequestHandler<CodeVerifyRequest, CodeVerifyResponse>
{
    public Task<CodeVerifyResponse> Handle(CodeVerifyRequest request, CancellationToken cancellationToken)
    {
        string codeword = configuration["Auth:Codeword"] ?? "";

        string offered = speech.AuthConsume();

        bool ok = codeword.Length == 0 || offered == codeword;

        if (ok) speech.Authorize();

        logger.LogInformation("code: {Verdict}", ok ? "accepted, server authorized" : "rejected");

        return Task.FromResult(new CodeVerifyResponse { Bound = ok });
    }
}
