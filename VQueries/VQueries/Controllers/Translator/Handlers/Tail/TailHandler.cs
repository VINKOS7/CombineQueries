using MediatR;

using CombineQueries.Api.Services.AFST;
using CombineQueries.Api.Services.Forwarder;
using CombineQueries.Domain.Aggregates.Translator;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Tail;

public class TailHandler : IRequestHandler<TailRequest, TailResponse>
{
    private readonly ILogger<TailHandler> _logger;
    private readonly ISpeach _speach;
    private readonly IForwarder _forwarder;

    public TailHandler(ILogger<TailHandler> logger, IForwarder forwarder, ISpeach afst)
    {
        _logger = logger;
        _forwarder = forwarder;
        _speach = afst;
    }

    public async Task<TailResponse> Handle(TailRequest request, CancellationToken cancellationToken)
    {
        if (_speach.Alphabet is null || _speach.RuneAlphabet is null) throw new Exception("CRIT: /init was not called");

        string tail = Translator.DecodeTail(request.Runes, _speach.RuneAlphabet, _speach.Alphabet, _speach.RuneSize, _speach.SymbolsOf(request.Type));

        var assembled = _speach.Close(tail, request.Type);

        if (string.IsNullOrEmpty(assembled.Text)) throw new Exception("domain error: nothing was assembled");

        string url = _speach.Scheme + "://" + assembled.Text;

        _logger.LogInformation($"tail: assembled {assembled.Runes} runes + {tail.Length} chars in {assembled.ElapsedMs} ms -> '{url}'");

        var forwarded = await _forwarder.GetAsync(url, cancellationToken);

        int handle = _speach.Intern(url, assembled.ElapsedMs + forwarded.ElapsedMs);

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
