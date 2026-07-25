using MediatR;

using CombineQueries.Api.Services.AFST;

namespace CombineQueries.Api.Controllers.Translator.Handlers.Count;

// Только объявляет, чего ждать. Ни склейки, ни сети - поэтому и зависимостей минимум.
public class CountHandler : IRequestHandler<CountRequest, CountResponse>
{
    private readonly ILogger<CountHandler> _logger;
    private readonly IAFST _afst;

    public CountHandler(ILogger<CountHandler> logger, IAFST afst)
    {
        _logger = logger;
        _afst = afst;
    }

    public Task<CountResponse> Handle(CountRequest request, CancellationToken cancellationToken)
    {
        if (_afst.Alphabet is null) throw new Exception("CRIT: /init не вызван");

        int k = request.Value / _afst.RuneSize;
        int pad = request.Value % _afst.RuneSize;

        if (k <= 0) throw new Exception($"domain error: кусков объявлено {k}");

        _afst.Expect(k, pad);

        _logger.LogInformation($"count: ждём {k} кусков, срезать {pad}");

        return Task.FromResult(new CountResponse { Expected = k, Pad = pad });
    }
}
