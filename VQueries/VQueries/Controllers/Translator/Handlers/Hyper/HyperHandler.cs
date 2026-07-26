using MediatR;

using CombineQueries.Api.Services.AFST;
using CombineQueries.Api.Services.Forwarder;

namespace CombineQueries.Api.Controllers.Translator.Handlers.Hyper;

// Быстрый путь: ссылка уже известна серверу по идентификатору, поэтому вся цепочка
// /n + K кусков схлопывается в ОДИН запрос. Отсюда и имя роута - /h, hyper.
public class HyperHandler : IRequestHandler<HyperRequest, HyperResponse>
{
    private readonly ILogger<HyperHandler> _logger;
    private readonly IAFST _afst;
    private readonly IForwarder _forwarder;

    public HyperHandler(ILogger<HyperHandler> logger, IForwarder forwarder, IAFST afst)
    {
        _logger = logger;
        _forwarder = forwarder;
        _afst = afst;
    }

    public async Task<HyperResponse> Handle(HyperRequest request, CancellationToken cancellationToken)
    {
        if (_afst.Alphabet is null) throw new Exception("CRIT: /init was not called");

        string? url = _afst.Resolve(request.Value);

        if (url is null)
        {
            _logger.LogWarning($"hyper: handle {request.Value} is unknown - client must resend the full url");

            return new HyperResponse { Known = false };
        }

        var forwarded = await _forwarder.GetAsync(url, cancellationToken);

        long first = _afst.FirstSendMsOf(request.Value);

        // Ради этой строки всё и делалось: слева полная цена первой отправки (сборка + форвард),
        // справа - повторной (только форвард). Величины сопоставимые, поэтому отношение честное.
        //
        // Две оговорки, без которых число обманет. Первая: обе цифры СЕРВЕРНЫЕ, round-trip'ы до
        // Udon сюда не входят - у клиента разрыв будет БОЛЬШЕ, там 7 загрузок против 1, каждая под
        // рейт-лимитом VRChat. Вторая: форвард наружу есть в обеих цифрах и шумит на сотни мс,
        // так что на одном прогоне отношение скачет. Устойчивая часть выигрыша - убранная сборка.
        _logger.LogInformation(
            $"hyper: handle {request.Value} -> '{url}' | 1 request, {forwarded.ElapsedMs} ms "
            + $"vs first send {first} ms"
            + (first > 0 ? $" -> x{(double)first / Math.Max(forwarded.ElapsedMs, 1):F1}" : ""));

        return new HyperResponse
        {
            Known = true,
            ForwardedUrl = url,
            Response = forwarded.Body,
            ElapsedMs = forwarded.ElapsedMs,
            FirstSendMs = first
        };
    }
}
