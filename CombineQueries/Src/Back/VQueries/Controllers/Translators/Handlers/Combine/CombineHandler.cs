using MediatR;

using CombineQueries.Api.Services.Outbox;
using CombineQueries.Api.Services.Speech;
using CombineQueries.Api.Controllers.Accounts;
using CombineQueries.Api.Controllers.Translators.Handlers.Tail;
using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Combine;

// Один /c на всё. VF и hop несут подпись - по ней кусок ложится в сборку своего потока. Руна и пара
// её не несут (их ссылки в билде без подписи) - их поток тот, что подписывал последним. Первую /c
// сервер узнаёт сам, по пустой сборке потока: руна без метки «:» на конце и пара - весь адрес,
// закрываются сразу, как хвост. Отсюда ответ двух видов: кусок или закрытие.
public class CombineHandler(ILogger<CombineHandler> logger, ISpeech speech, IOutbox outbox, ISender sender) : IRequestHandler<CombineRequest, object>
{
    // Вид ссылки: руна, фрагмент (VF и hop), пара «фрагмент + одна-две буквы».
    private const int Rune = 0;
    private const int Pair = 2;

    public async Task<object> Handle(CombineRequest request, CancellationToken cancellationToken)
    {
        if (speech.Alphabet is null || speech.RuneAlphabet is null) throw new Exception("CombineHandler: /connect was not called");

        if (string.IsNullOrEmpty(request.Runes)) throw new Exception("CombineHandler: empty runes");

        // Пара и одиночная f/c несут весь адрес сами - собираются в своей одноразовой сборке (Isolate).
        // В усыновлённом потоке пара выбрасывала чужие куски (адрес без склейки сбрасывает сборку), а
        // хвост хозяина закрывал пустоту - https://41; и обе забирали чужой долг.
        if (request.Sign < 0)
        {
            if (request.Q == Pair)
            {
                speech.Isolate();

                return await sender.Send(new TailRequest { Runes = request.Runes, Fragment = request.Page * speech.DfaSize + request.Id, Merge = false, Pair = true, Type = TypeQuery.Fragmentate }, cancellationToken);
            }

            speech.Adopt();

            if (request.Q == Rune && speech.Single(request.Runes))
            {
                speech.Isolate();

                return await sender.Send(new TailRequest { Runes = "", Rune = request.Runes, Type = TypeQuery.Fragmentate }, cancellationToken);
            }

            if (request.Q != Rune) throw new Exception("auth error: combine without sign");
        }
        else if (!speech.CheckSign(request.Sign))
        {
            logger.LogWarning("combine: sign {Sign} rejected, not in the expected parts", request.Sign);

            throw new Exception("auth error: combine sign rejected");
        }

        int received;

        if (request.Q == Rune)
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
            case Speech.VFInfinite: logger.LogInformation("combine: hop {Hop} took Infinite child of the last VF", request.Hop);
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

        int box = request.Sign < 0 ? 0 : speech.Stream;

        var answer = new CombineResponse { Received = received, Ready = outbox.Take(box), Pending = outbox.Pending(box) };

        return request.Sign < 0 ? answer : answer.Signed(speech);
    }
}
