using MediatR;

using CombineQueries.Api.Services.AFST;
using CombineQueries.Api.Services.Forwarder;

namespace CombineQueries.Api.Controllers.Translator.Handlers.Combine;

// Принимает кусок и склеивает. Отдельного "отправляй" в протоколе больше нет: сколько кусков
// будет, объявлено заранее через /n, и как только набралось - здесь же идёт форвардинг.
// Собранная ссылка интернируется, и её идентификатор уезжает клиенту в ответе: со второго раза
// та же ссылка отправляется ОДНИМ запросом через /h.
public class CombineHandler : IRequestHandler<CombineRequest, CombineResponse>
{
    private readonly ILogger<CombineHandler> _logger;
    private readonly IAFST _afst;
    private readonly IForwarder _forwarder;

    public CombineHandler(ILogger<CombineHandler> logger, IForwarder forwarder, IAFST afst)
    {
        _logger = logger;
        _forwarder = forwarder;
        _afst = afst;
    }

    public async Task<CombineResponse> Handle(CombineRequest request, CancellationToken cancellationToken)
    {
        if (_afst.Alphabet is null) throw new Exception("CRIT: /init was not called");
        if (string.IsNullOrEmpty(request.Runes)) throw new Exception("domain error: empty runes");

        var rune = _afst.Accept(request.Runes);

        if (!rune.Complete)
        {
            _logger.LogInformation($"combine: rune {rune.Received}/{rune.Expected}, {rune.ElapsedMs} ms so far");

            return new CombineResponse { Received = rune.Received, Expected = rune.Expected };
        }

        string url = rune.Text!;

        _logger.LogInformation($"combine: assembled {rune.Received} runes in {rune.ElapsedMs} ms -> '{url}'");

        var forwarded = await _forwarder.GetAsync(url, cancellationToken);

        // Интернируем ПОСЛЕ форварда, чтобы эталон был полным: сборка + поход наружу.
        // Иначе /h сравнивал бы своё время (форвард) со временем одной лишь сборки - разные
        // величины, и отношение получалось бы бессмысленным.
        int handle = _afst.Intern(url, rune.ElapsedMs + forwarded.ElapsedMs);

        _logger.LogInformation(
            $"combine: first send took {rune.ElapsedMs + forwarded.ElapsedMs} ms total "
            + $"({rune.Received + 1} requests: assembly {rune.ElapsedMs} ms + forward {forwarded.ElapsedMs} ms), handle {handle}");

        return new CombineResponse
        {
            Received = rune.Received,
            Expected = rune.Expected,
            Complete = true,
            ForwardedUrl = url,
            Response = forwarded.Body,
            Handle = handle,
            AssemblyMs = rune.ElapsedMs,
            ForwardMs = forwarded.ElapsedMs
        };
    }
}
