using MediatR;

using CombineQueries.Api.Services.Outbox;
using CombineQueries.Api.Services.Speech;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Combine;

public class CombineHandler(ILogger<CombineHandler> logger, ISpeech speech, IOutbox outbox) : IRequestHandler<CombineRequest, CombineResponse>
{
    public Task<CombineResponse> Handle(CombineRequest request, CancellationToken cancellationToken)
    {
        if (speech.Alphabet is null || speech.RuneAlphabet is null) throw new Exception("CombineHandler: /connect was not called");

        if (string.IsNullOrEmpty(request.Runes)) throw new Exception("CombineHandler: empty runes");

        int received;

        if (request.Q == 0)
        {
            speech.PushDirectRunes(request.Runes);

            received = speech.Accept(request.Runes);
        }
        else if (request.Hop > 0) received = speech.Hop(request.Hop);
        else
        {
            int gid = request.Page * speech.DfaSize + request.Id;

            if (speech.ResolveVirtualFragment(gid) is null)
            {
                speech.Fault($"unknown VF page={request.Page} off={request.Id}");

                logger.LogWarning("combine: VF page={Page} off={Id} unknown, stream dropped until connect", request.Page, request.Id);

                received = -1;
            }
            else
            {
                speech.SetFragmentPage(request.Page);

                received = speech.AcceptVirtualFragment(request.Id);
            }
        }

        switch (received)
        {
            case Speech.VFL2: logger.LogInformation("combine: VF {Id} accepted from L2", request.Id);
                break;
            case Speech.VFL3: logger.LogInformation("combine: VF {Id} accepted from L3", request.Id);
                break;
            case Speech.VFInfinite: logger.LogInformation("combine: VF hopped to Infinite link");
                break;
            case Speech.VFBroken: 
                speech.Fault($"hop {request.Hop} broke the chain");
                logger.LogWarning("combine: hop {Hop} broke the chain, stream dropped until connect", request.Hop);
                break;
            case -1: logger.LogWarning("combine: VF page={Page} off={Id} unknown, stream dropped until connect", request.Page, request.Id);
                break;
            default: logger.LogInformation("combine: rune {Received} accepted", received);
                break;
        }

        // Долг цепляем и сюда: он копится между запросами, а комбайнов в сборке больше всего -
        // значит через них он и доедет раньше всего.
        var ready = outbox.Take(speech.Stream);

        return Task.FromResult(new CombineResponse { Received = received, Ready = ready, Pending = outbox.Pending(speech.Stream) });
    }
}
