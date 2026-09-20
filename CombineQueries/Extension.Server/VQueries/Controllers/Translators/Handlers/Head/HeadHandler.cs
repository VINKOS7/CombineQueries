using MediatR;

using CombineQueries.Api.Services.Outbox;
using CombineQueries.Api.Services.Speech;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Head;

public class HeadHandler(ILogger<HeadHandler> logger, IOutbox outbox, ISpeech speech) : IRequestHandler<HeadRequest, HeadResponse>
{
    private const int Candidates = 8;

    public Task<HeadResponse> Handle(HeadRequest request, CancellationToken cancellationToken)
    {
        if (speech.Alphabet is null) throw new Exception("CRIT: /connect was not called");

        if (!speech.CheckSign(request.Sign))
        {         
            logger.LogWarning("head: sign {Sign} rejected, not in the expected parts", request.Sign);

            throw new Exception("auth error: head sign rejected");
        }

        string? text = speech.ResolveVirtualFragment(request.Fragment);

        if (string.IsNullOrEmpty(text))
        {
            logger.LogWarning("head: fragment {Fragment} is unknown", request.Fragment);

            return Task.FromResult(new HeadResponse { Known = false });
        }

        var found = speech.ChainsFrom(request.Base, text, Candidates).ToList();

        if (found.Count == 0)
        {
            speech.Keep(text);
            
            logger.LogInformation("head: '{Text}' is not in any known address - kept as the tail piece, client assembles the rest", text);

            return Task.FromResult(new HeadResponse { Known = false, Kept = text });
        }

        var ready = outbox.Take(speech.Stream);

        logger.LogInformation("head: '{Text}' -> {Urls} found, {Ready} ready now, {Pending} in flight", text, found.Count, ready.Count, outbox.Pending(speech.Stream));

        return Task.FromResult(new HeadResponse
        {
            Known = true,
            Urls = found.Count,
            Found = found.Select(chain => new Found(speech.Scheme + "://" + chain.Url, chain.Jump)).ToList(),
            Ready = ready,
            Pending = outbox.Pending(speech.Stream)
        });
    }
}
