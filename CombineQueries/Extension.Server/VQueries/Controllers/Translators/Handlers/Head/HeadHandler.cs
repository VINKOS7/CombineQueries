using MediatR;

using CombineQueries.Api.Controllers.Translators.Handlers.Hyper;
using CombineQueries.Api.Services.Outbox;
using CombineQueries.Api.Services.Speech;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Head;

// Голова потока: «вот кусок моего адреса, ты его знаешь?»
//
// Отвечает тем же, чем /h/ - собранными адресами и их телами. Разница в том, ЧЕМ спрашивают:
// прыжок называет номер (клиент его знает), голова - содержимое (номера клиент не знает).
//
// Найденных кандидатов форвардим ВСЕХ и отдаём словарём: клиент забирает из него свой адрес, а
// остальные кладёт в кольцо. Иначе «нашлось несколько» стоило бы второго запроса, а весь смысл
// головы в том, чтобы уложиться в один.
public class HeadHandler(ILogger<HeadHandler> logger, IOutbox outbox, ISpeech speech) : IRequestHandler<HeadRequest, HeadResponse>
{
    // Сколько адресов называем за раз. Потолок нужен потому, что кусок вроде "?limit" сидит в
    // сотнях адресов, и без него ответ распух бы на ровном месте.
    //
    // Восемь, а не четыре: наружу голова не ходит, лишнее имя стоит строчки в ответе и дарит
    // клиенту чужой номер. База стала на бит грубее - под маску попадает вдвое больше листов,
    // и потолок обязан был подрасти следом, иначе половина найденного просто отсекалась бы.
    private const int Candidates = 8;

    public Task<HeadResponse> Handle(HeadRequest request, CancellationToken cancellationToken)
    {
        if (speech.Alphabet is null) throw new Exception("CRIT: /connect was not called");

        // Подпись сверяется ДО всего: голова отдаёт наружу собранные адреса, то есть по правам
        // равна хвосту. Попытка ровно одна - дальше приём валится до connect.
        if (!speech.CheckSign(request.Sign))
        {
            speech.Fault($"head sign {request.Sign} rejected");

            logger.LogWarning("head: sign {Sign} rejected, stream dropped until connect", request.Sign);

            throw new Exception("auth error: head sign rejected");
        }

        string? text = speech.ResolveVirtualFragment(request.Fragment);

        if (string.IsNullOrEmpty(text))
        {
            logger.LogWarning("head: fragment {Fragment} is unknown", request.Fragment);

            return Task.FromResult(new HeadResponse { Known = false });
        }

        // Сложение: гипер даёт начало, кусок - расхождение. Сервер строит точный адрес и ищет
        // именно его - без базы голова не зовётся вовсе, поэтому второй ветки тут нет.
        var found = speech.ChainsFrom(request.Base, text, Candidates).ToList();

        if (found.Count == 0)
        {
            // Промах - НЕ потеря. Кусок к нам уже приехал, запрос за него оплачен, поэтому держим
            // его до закрытия: клиенту останется досказать только начало адреса, а конец у сервера
            // уже есть. Иначе за один и тот же кусок платили бы дважды.
            speech.Keep(text);

            logger.LogInformation("head: '{Text}' is not in any known address - kept as the tail piece, client assembles the rest", text);

            return Task.FromResult(new HeadResponse { Known = false, Kept = text });
        }

        // Голова НАРУЖУ НЕ ХОДИТ. Её дело - назвать: вот адреса и вот их номера. Забирает их
        // прыжок, он для того и есть; смешивать поиск с доставкой значит ходить за телами, которых
        // могли и не просить. Долг она при этом несёт как любой ответ - но своего не создаёт.
        var ready = outbox.Take();

        logger.LogInformation("head: '{Text}' -> {Urls} found, {Ready} ready now, {Pending} in flight",
            text, found.Count, ready.Count, outbox.Pending);

        return Task.FromResult(new HeadResponse
        {
            Known = true,
            Urls = found.Count,
            Found = found.Select(chain => new Found(speech.Scheme + "://" + chain.Url, chain.Jump)).ToList(),
            Ready = ready,
            Pending = outbox.Pending
        });
    }
}
