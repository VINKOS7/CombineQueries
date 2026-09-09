using MediatR;

using CombineQueries.Api.Services.Outbox;
using CombineQueries.Api.Services.Speech;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Hyper;

// Хайпер обязан укладываться в ОДИН запрос - ради этого он и существует. Не уложился (адрес
// неизвестен) - клиент идёт обычной дорогой: combine + tail по динамической фрагментации.
public class HyperHandler(ILogger<HyperHandler> logger, IOutbox outbox, ISpeech speech) : IRequestHandler<HyperRequest, HyperResponse>
{
    // Сколько адресов отдаём за один запрос диапазоном.
    private const int Batch = 8;

    public Task<HyperResponse> Handle(HyperRequest request, CancellationToken cancellationToken)
    {
        if (speech.Alphabet is null) throw new Exception("CRIT: /connect was not called");

        // Подпись сверяется ДО всего: прыжок отдаёт наружу собранный URL, то есть по правам он
        // равен хвосту - и подпись у него такая же полная. Попытка ровно одна.
        if (!speech.CheckSign(request.Sign))
        {
            speech.Fault($"hyper sign {request.Sign} rejected");

            logger.LogWarning("hyper: sign {Sign} rejected, stream dropped until connect", request.Sign);

            throw new Exception("auth error: hyper sign rejected");
        }

        // Лист несёт адрес целиком (хвост входит в путь) - значит отдавать есть что.
        //
        // Наружу идём В ФОН: клиента держать нельзя, форвард занимает секунды. Отвечаем ДОЛГОМ -
        // тем, что доспело к этому мгновению; не доспело ничего - долг пустой, приедет со
        // следующим запросом, каким бы эндпоинтом он ни был.
        // count = 0 значит «ничего не запрашивай, только отдай долг» - тем, кому результат нужен
        // немедленно, приходится ждать, и вот чем ждут. Номер при этом не важен.
        int count = Math.Clamp(request.Count, 0, Batch);

        if (count == 0)
        {
            var settled = outbox.Take();

            logger.LogInformation("hyper: debt asked, {Ready} ready now, {Pending} in flight", settled.Count, outbox.Pending);

            return Task.FromResult(new HyperResponse { Known = true, Urls = 0, Ready = settled, Pending = outbox.Pending });
        }

        var urls = new List<string>();

        for (int at = 0; at < count; at++)
        {
            string? url = speech.UrlOf(request.Value + at);

            if (url is null) continue;

            string full = speech.Scheme + "://" + url;

            urls.Add(full);

            outbox.Fetch(full);
        }

        if (urls.Count > 0)
        {
            var ready = outbox.Take();

            logger.LogInformation("hyper: jump {Jump}{Range} -> {Urls} urls sent, {Ready} ready now, {Pending} in flight",
                request.Value, count > 1 ? "+" + count : "", urls.Count, ready.Count, outbox.Pending);

            return Task.FromResult(new HyperResponse
            {
                Known = true,
                Urls = urls.Count,
                ForwardedUrl = urls[0],
                Ready = ready,
                Pending = outbox.Pending
            });
        }

        // Промежуточный узел: адреса у него нет, отдать нечего. Поднимаем куски и ждём, что клиент
        // дошлёт расхождение и закроет хвостом - это уже не «за один запрос», а фолбэк.
        int restored = speech.Resume(request.Value);

        if (restored < 0)
        {
            // Номера нет: базу сбросили, мир переехал на другой сервер или прыжок вообще чужой.
            // Причина неважна - ответ один: собрать адрес фрагментами, а хвост его проиндексирует.
            logger.LogWarning("hyper: jump {Jump} is unknown - assemble instead, tail will index it", request.Value);

            return Task.FromResult(new HyperResponse { Known = false, Note = "unsaved hyper, saved for next hyper" });
        }

        logger.LogInformation("hyper: jump {Jump} resumed {Restored} combine steps", request.Value, restored);

        return Task.FromResult(new HyperResponse { Known = true, Resumed = restored });
    }

}
