using MediatR;

using CombineQueries.Api.Services.Outbox;
using CombineQueries.Api.Services.Speech;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Credit;

public class CreditHandler(ILogger<CreditHandler> logger, IOutbox outbox, ISpeech speech) : IRequestHandler<CreditRequest, CreditResponse>
{
    public Task<CreditResponse> Handle(CreditRequest request, CancellationToken cancellationToken)
    {
        if (speech.Alphabet is null) throw new Exception("CRIT: /connect was not called");

        if (!speech.CheckSign(request.Sign))
        {
            // Приём НЕ роняем: часть чужая, а не поток разъехался. У остальных клиентов свои
            // кольца, и падать им из-за чужого запроса незачем.

            logger.LogWarning("credit: sign {Sign} rejected, not in the expected parts", request.Sign);

            throw new Exception("auth error: credit sign rejected");
        }

        var ready = outbox.Take(speech.Stream);

        logger.LogInformation("credit: {Ready} paid now, {Pending} still in flight", ready.Count, outbox.Pending(speech.Stream));

        return Task.FromResult(new CreditResponse { Ready = ready, Pending = outbox.Pending(speech.Stream) });
    }
}
