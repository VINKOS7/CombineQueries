using MediatR;

using CombineQueries.Api.Services.Speech;
using CombineQueries.Api.Services.Auth;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Code;

public class CodeVerifyHandler(IHttpContextAccessor http, IConfiguration configuration, ILogger<CodeVerifyHandler> logger, ISpeech speech)
    : IRequestHandler<CodeVerifyRequest, CodeVerifyResponse>
{
    public Task<CodeVerifyResponse> Handle(CodeVerifyRequest request, CancellationToken cancellationToken)
    {
        string key = http.HttpContext is null ? "" : MasterGate.KeyOf(http.HttpContext);

        string codeword = configuration["Auth:Codeword"] ?? "";

        string offered = speech.AuthConsume(key);

        bool ok = codeword.Length == 0 || offered == codeword;

        bool bound = ok && speech.BindMaster(key);

        logger.LogInformation("code: {Ip} {Verdict}", key, bound ? "accepted, master bound" : "rejected");

        return Task.FromResult(new CodeVerifyResponse { Bound = bound });
    }
}
