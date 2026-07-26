using MediatR;

using CombineQueries.Api.Services.AFST;

namespace CombineQueries.Api.Controllers.Translator.Handlers.Tail;

// Только объявляет, чего ждать. Ни склейки, ни сети - поэтому и зависимостей минимум.
public class TailHandler : IRequestHandler<TailRequest, TailResponse>
{
    private readonly ILogger<TailHandler> _logger;
    private readonly IAFST _afst;

    public TailHandler(ILogger<TailHandler> logger, IAFST afst)
    {
        _logger = logger;
        _afst = afst;
    }

    public Task<TailResponse> Handle(TailRequest request, CancellationToken cancellationToken)
    {
        if (_afst.Alphabet is null) throw new Exception("CRIT: /init was not called");

        int k = request.Value / _afst.RuneSize;
        int pad = request.Value % _afst.RuneSize;

        if (k <= 0) throw new Exception($"domain error: declared rune count is {k}");

        _afst.Expect(k, pad);

        _logger.LogInformation($"head: expecting {k} runes, trimming {pad} trailing chars");

        return Task.FromResult(new TailResponse { Expected = k, Pad = pad });
    }
}
