using MediatR;

using CombineQueries.Api.Services.Speech;
using CombineQueries.Api.Services.Forwarder;
using CombineQueries.Domain.Aggregates.Translator;
using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Tail;

public class TailHandler : IRequestHandler<TailRequest, TailResponse>
{
    private readonly ILogger<TailHandler> _logger;
    private readonly ISpeech _speach;
    private readonly IForward _forwarder;

    public TailHandler(ILogger<TailHandler> logger, IForward forwarder, ISpeech speech)
    {
        _logger = logger;
        _forwarder = forwarder;
        _speach = speech;
    }

    public async Task<TailResponse> Handle(TailRequest request, CancellationToken cancellationToken)
    {
        if (_speach.Alphabet is null || _speach.RuneAlphabet is null) throw new Exception("CRIT: /init was not called");

        var handle = 0;
        var tail = string.Empty;
        var url = string.Empty;

        AssembledResult? assembled = null;
        ForwardResult? forwarded = null;


        switch (request.Type)
        {
            case TypeCombine.Direct:
                tail = Translator.FragmentateUnrune(request.Runes, _speach.RuneAlphabet, _speach.Alphabet, _speach.RuneSize, _speach.SymbolsOf(request.Type));

                assembled = _speach.Close(tail, request.Type);

                if (string.IsNullOrEmpty(assembled.Text)) throw new Exception("domain error: nothing was assembled");

                url = _speach.Scheme + "://" + assembled.Text;

                _logger.LogInformation($"tail: assembled {assembled.Runes} runes + {tail.Length} chars in {assembled.ElapsedMs} ms -> '{url}'");

                forwarded = await _forwarder.GetAsync(url, cancellationToken);

                handle = _speach.Intern(url, assembled.ElapsedMs + forwarded.ElapsedMs);

                // _speach.DropSB();

                _logger.LogInformation($"tail: first send took {assembled.ElapsedMs + forwarded.ElapsedMs} ms total " + $"({assembled.Runes + 1} requests), handle {handle}");
                
                break;

            case TypeCombine.Fragmentate:
                tail = Translator.FragmentateUnrune(request.Runes, _speach.RuneAlphabet, _speach.Alphabet, _speach.RuneSize, _speach.SymbolsOf(request.Type));

                assembled = _speach.Close(tail, request.Type);

                if (string.IsNullOrEmpty(assembled.Text)) throw new Exception("domain error: nothing was assembled");

                url = _speach.Scheme + "://" + assembled.Text;

                _logger.LogInformation($"tail: assembled {assembled.Runes} runes + {tail.Length} chars in {assembled.ElapsedMs} ms -> '{url}'");

                forwarded = await _forwarder.GetAsync(url, cancellationToken);

                handle = _speach.Intern(url, assembled.ElapsedMs + forwarded.ElapsedMs);

                // _speach.DropSB();

                _logger.LogInformation($"tail: first send took {assembled.ElapsedMs + forwarded.ElapsedMs} ms total " + $"({assembled.Runes + 1} requests), handle {handle}");

                break;
            default: throw new ArgumentOutOfRangeException(nameof(request.Type), request.Type, "Unexpected TypeCombine value");
                // кекичlol
        }

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
