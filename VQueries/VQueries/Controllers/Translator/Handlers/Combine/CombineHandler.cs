using MediatR;

using CombineQueries.Api.Services.Speech;
using CombineQueries.Domain.Aggregates.Translator;
using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Combine;

public class CombineHandler : IRequestHandler<CombineRequest, CombineResponse>
{
    private readonly ILogger<CombineHandler> _logger;
    private readonly ISpeech _speech;

    public CombineHandler(ILogger<CombineHandler> logger, ISpeech speech)
    {
        _logger = logger;
        _speech = speech;
    }

    public Task<CombineResponse> Handle(CombineRequest request, CancellationToken cancellationToken)
    {
        if (_speech.Alphabet is null || _speech.RuneAlphabet is null) throw new Exception("CombineHandler: /init was not called");
        if (string.IsNullOrEmpty(request.Runes)) throw new Exception("CombineHandler: empty runes");

        int symbols = _speech.SymbolsOf(TypeCombine.Fragmentate);

        bool fragmentate = Translator.HasFragment(request.Runes, _speech.RuneAlphabet, _speech.Alphabet, _speech.RuneSize, symbols);

        if (!fragmentate) _speech.PushDirectRunes(request.Runes);

        // походу вообще внутри Accept не нужно проверять на фрагменты, так как мы уже проверили выше и ��сли фрагменты есть, то мы их не пушим в очередь, а если нет, то пушим
        int received = _speech.Accept(request.Runes);

        _logger.LogInformation($"combine: rune {received} accepted, {(fragmentate ? "with fragments" : "direct")}");

        return Task.FromResult(new CombineResponse { Received = received });
    }
}