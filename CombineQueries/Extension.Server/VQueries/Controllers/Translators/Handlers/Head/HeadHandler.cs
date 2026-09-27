using MediatR;

using CombineQueries.Api.Services.Outbox;
using CombineQueries.Api.Services.Speech;
using CombineQueries.Api.Controllers.Accounts;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Head;

// Голова - прыжок для адреса, номера которого клиент не знает: гипер даёт начало, фрагмент -
// расхождение. Нашёлся адрес - сервер сам идёт за ним наружу, как /h, и тело едет этим же ответом.
// Раньше голова только называла номер, и за телом уходил второй запрос - ещё пять секунд.
public class HeadHandler(ILogger<HeadHandler> logger, IOutbox outbox, ISpeech speech) : IRequestHandler<HeadRequest, HeadResponse>
{
    private const int Candidates = 8;

    // Как у прыжка: ждём поход наружу коротко, чтобы тела уехали этим же ответом.
    private const int Grace = 500;
    private const int Step = 25;

    public async Task<HeadResponse> Handle(HeadRequest request, CancellationToken cancellationToken) => (await Answer(request, cancellationToken)).Signed(speech);

    private async Task<HeadResponse> Answer(HeadRequest request, CancellationToken cancellationToken)
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

            return Missed(null);
        }

        var found = speech.ChainsFrom(request.Base, text, Candidates).ToList();

        if (found.Count == 0)
        {
            // Кусок полной головы - конец адреса, клиент доскажет начало. У неполной он середина,
            // и приклеить его концом значило бы собрать чужой адрес.
            if (request.Complete) speech.Keep(text);

            logger.LogInformation("head: '{Text}' is not in any known address - {Kept}", text, request.Complete ? "kept as the tail piece, client assembles the rest" : "nothing kept");

            return Missed(request.Complete ? text : null);
        }

        // Адреса определяет сервер: каждый найденный делится по хосту. Все, кроме последнего,
        // полные - за ними сразу наружу. Последний решает флаг клиента.
        var urls = found.SelectMany(chain => speech.Split(chain.Url)).Distinct().ToList();

        string? carried = null;

        if (!request.Complete)
        {
            // Неполный последний - это начало, остаток дошлёт следующий запрос. Начало обязано быть
            // одно: из нескольких кандидатов сервер не знает, какое продолжать.
            if (found.Count > 1)
            {
                logger.LogWarning("head: '{Text}' -> {Urls} starts for an incomplete address - ambiguous, nothing kept", text, found.Count);

                return Missed(null);
            }

            carried = urls[^1];

            urls.RemoveAt(urls.Count - 1);

            speech.Carry(carried);
        }

        foreach (string url in urls) outbox.Fetch(speech.Scheme + "://" + url, speech.Stream);

        var ready = new List<Delivery>(outbox.Take(speech.Stream));

        int waited = 0;

        while (ready.Count < urls.Count && waited < Grace && outbox.Pending(speech.Stream) > 0)
        {
            await Task.Delay(Step, cancellationToken);

            waited += Step;

            ready.AddRange(outbox.Take(speech.Stream));
        }

        logger.LogInformation("head: '{Text}' -> {Urls} urls sent ({Sent}){Carried}, {Ready} ready after {Waited} ms, {Pending} in flight",
            text, urls.Count, string.Join(", ", urls), carried is null ? "" : ", carried " + carried, ready.Count, waited, outbox.Pending(speech.Stream));

        return new HeadResponse
        {
            Known = true,
            Urls = found.Count,
            Found = found.Select(chain => new Found(speech.Scheme + "://" + chain.Url, chain.Jump)).ToList(),
            Carried = carried is not null,
            Ready = ready,
            Pending = outbox.Pending(speech.Stream)
        };
    }

    // Адреса нет, но долг едет довеском к любому ответу.
    private HeadResponse Missed(string? kept) => new()
    {
        Known = false,
        Kept = kept,
        Ready = outbox.Take(speech.Stream),
        Pending = outbox.Pending(speech.Stream)
    };
}
