using MediatR;

using CombineQueries.Api.Services.AFST;
using CombineQueries.Api.Services.Forwarder;

namespace CombineQueries.Api.Controllers.Translator.Handlers.Tail;

public class TailHandler : IRequestHandler<TailRequest, TailResponse>
{
    private readonly ILogger<TailHandler> _logger;
    private readonly IAFST _afst;
    private readonly IForwarder _forwarder;

    public TailHandler(ILogger<TailHandler> logger, IForwarder forwarder, IAFST afst)
    {
        _logger = logger;
        _forwarder = forwarder;
        _afst = afst;
    }

    public async Task<TailResponse> Handle(TailRequest request, CancellationToken cancellationToken)
    {
        if (_afst.Alphabet is null || _afst.WireAlphabet is null) throw new Exception("CRIT: /init was not called");

        string tail = Domain.Aggregates.Translator.Translator.DecodeTail(request.Runes, _afst.WireAlphabet, _afst.Alphabet);

        var assembled = _afst.Close(tail);

        string url = assembled.Text;

        if (string.IsNullOrEmpty(url)) throw new Exception("domain error: nothing was assembled");

        _logger.LogInformation($"tail: assembled {assembled.Runes} runes + {tail.Length} chars in {assembled.ElapsedMs} ms -> '{url}'");

        var forwarded = await _forwarder.GetAsync(url, cancellationToken);

        int handle = _afst.Intern(url, assembled.ElapsedMs + forwarded.ElapsedMs);

        _logger.LogInformation(
            $"tail: first send took {assembled.ElapsedMs + forwarded.ElapsedMs} ms total "
            + $"({assembled.Runes + 1} requests), handle {handle}");

        return new TailResponse
        {
            Runes = assembled.Runes,
            ForwardedUrl = url,
            Response = forwarded.Body,
            Handle = handle,
            AssemblyMs = assembled.ElapsedMs,
            ForwardMs = forwarded.ElapsedMs
        };
    }
}
