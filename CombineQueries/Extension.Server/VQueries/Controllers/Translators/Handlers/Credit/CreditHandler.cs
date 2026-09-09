using MediatR;

using CombineQueries.Api.Services.Outbox;
using CombineQueries.Api.Services.Speech;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Credit;

// Погашение долга. Ничего не запрашивает и ничего не собирает - только отдаёт доспевшее.
//
// Подпись сверяем как везде: долг это чужие тела, отдавать их кому попало нельзя, и позиция кольца
// обязана двигаться в ногу с клиентом - иначе следующий хвост уедет с чужим номером.
public class CreditHandler(ILogger<CreditHandler> logger, IOutbox outbox, ISpeech speech) : IRequestHandler<CreditRequest, CreditResponse>
{
    public Task<CreditResponse> Handle(CreditRequest request, CancellationToken cancellationToken)
    {
        if (speech.Alphabet is null) throw new Exception("CRIT: /connect was not called");

        if (!speech.CheckSign(request.Sign))
        {
            speech.Fault($"credit sign {request.Sign} rejected");

            logger.LogWarning("credit: sign {Sign} rejected, stream dropped until connect", request.Sign);

            throw new Exception("auth error: credit sign rejected");
        }

        var ready = outbox.Take();

        logger.LogInformation("credit: {Ready} paid now, {Pending} still in flight", ready.Count, outbox.Pending);

        return Task.FromResult(new CreditResponse { Ready = ready, Pending = outbox.Pending });
    }
}
