using MediatR;

using CombineQueries.Api.Services.Forwarder;
using CombineQueries.Api.Services.Speech;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Hyper;

// Хайпер обязан укладываться в ОДИН запрос - ради этого он и существует. Не уложился (адрес
// неизвестен) - клиент идёт обычной дорогой: combine + tail по динамической фрагментации.
public class HyperHandler(ILogger<HyperHandler> logger, IForward forwarder, ISpeech speech) : IRequestHandler<HyperRequest, HyperResponse>
{
    public async Task<HyperResponse> Handle(HyperRequest request, CancellationToken cancellationToken)
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

        // Лист несёт адрес целиком (хвост входит в путь) - значит отдаём результат прямо здесь.
        string? url = speech.UrlOf(request.Value);

        if (url is not null)
        {
            // В дереве лежит адрес БЕЗ схемы - ровно так его собирает Close. Схему добавляет тот,
            // кто идёт наружу, как это делает и хвост.
            string full = speech.Scheme + "://" + url;

            var forwarded = await forwarder.GetAsync(full, cancellationToken);

            int handle = speech.Intern(full, forwarded.ElapsedMs);

            logger.LogInformation("hyper: jump {Jump} -> {Url} in one query ({ElapsedMs} ms)", request.Value, full, forwarded.ElapsedMs);

            return new HyperResponse
            {
                Known = true,
                ForwardedUrl = full,
                Response = forwarded.Body,
                ElapsedMs = forwarded.ElapsedMs,
                FirstSendMs = speech.FirstSendMsOf(handle)
            };
        }

        // Промежуточный узел: адреса у него нет, отдать нечего. Поднимаем куски и ждём, что клиент
        // дошлёт расхождение и закроет хвостом - это уже не «за один запрос», а фолбэк.
        int restored = speech.Resume(request.Value);

        if (restored < 0)
        {
            // Номера нет: базу сбросили, мир переехал на другой сервер или прыжок вообще чужой.
            // Причина неважна - ответ один: собрать адрес фрагментами, а хвост его проиндексирует.
            logger.LogWarning("hyper: jump {Jump} is unknown - assemble instead, tail will index it", request.Value);

            return new HyperResponse { Known = false, Note = "unsaved hyper, saved for next hyper" };
        }

        logger.LogInformation("hyper: jump {Jump} resumed {Restored} combine steps", request.Value, restored);

        return new HyperResponse { Known = true, Resumed = restored };
    }
}
