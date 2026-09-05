using MediatR;

using CombineQueries.Api.Services.Speech;

namespace CombineQueries.Api.Controllers.Translators.Handlers.CombineNew;

// Объединённый combine, роут /c/{runes}/{id}/{q}: q = сколько с конца - фрагменты.
// single-unit: q=0 -> чанк (Accept руны, в т.ч. с L1-корнями); q>=1 -> один VF по id
// (AcceptVirtualFragment сам решает L2/L3). Паковка (q>1: n рун + k фрагментов) - второй коммит.
public class CombineHandler(ILogger<CombineHandler> logger, ISpeech speech) : IRequestHandler<CombineRequest, CombineResponse>
{
    public Task<CombineResponse> Handle(CombineRequest request, CancellationToken cancellationToken)
    {
        if (speech.Alphabet is null || speech.RuneAlphabet is null) throw new Exception("CombineHandler: /init was not called");

        if (string.IsNullOrEmpty(request.Runes)) throw new Exception("CombineHandler: empty runes");

        int received;

        if (request.Q == 0)
        {
            // кеш директов на всякий
            speech.PushDirectRunes(request.Runes);

            received = speech.Accept(request.Runes);
        }
        else
        {
            // Глобальный адрес VF = page*DfaSize + id (page=0 -> L2, page>0 -> L3).
            int gid = request.Page * speech.DfaSize + request.Id;

            if (speech.ResolveVirtualFragment(gid) is null)
            {
                // VF неизвестен: руна здесь сентинел, Accept-фолбэка нет -> фрагмент пропадёт, клиент
                // увидит битый URL на /t/ и переинициализируется.
                logger.LogWarning("combine: VF page={Page} off={Id} unknown, dropped", request.Page, request.Id);

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
            case Speech.VFL2:
                logger.LogInformation("combine: VF {Id} accepted from L2", request.Id);
                break;
            case Speech.VFL3:
                logger.LogInformation("combine: VF {Id} accepted from L3", request.Id);
                break;
            case -1:
                break;
            default:
                logger.LogInformation("combine: rune {Received} accepted", received);
                break;
        }

        return Task.FromResult(new CombineResponse { Received = received });
    }
}
