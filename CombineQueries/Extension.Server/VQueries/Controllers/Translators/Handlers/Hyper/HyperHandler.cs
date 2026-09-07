using MediatR;

using CombineQueries.Api.Services.Speech;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Hyper;

public class HyperHandler(ILogger<HyperHandler> logger, ISpeech speech) : IRequestHandler<HyperRequest, HyperResponse>
{
    public Task<HyperResponse> Handle(HyperRequest request, CancellationToken cancellationToken)
    {
        if (speech.Alphabet is null) throw new Exception("CRIT: /connect was not called");

        int restored = speech.Resume(request.Value);

        if (restored < 0)
        {
            logger.LogWarning("hyper: jump {Jump} is unknown - client must resend the chain", request.Value);

            return Task.FromResult(new HyperResponse { Known = false });
        }

        logger.LogInformation("hyper: jump {Jump} resumed {Restored} combine steps", request.Value, restored);

        return Task.FromResult(new HyperResponse { Known = true, Resumed = restored });
    }
}
