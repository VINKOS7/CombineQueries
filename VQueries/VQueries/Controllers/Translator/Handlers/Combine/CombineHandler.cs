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
        if(_speech.Alphabet is null || string.IsNullOrEmpty(request.Runes)) throw new Exception("CombineHandler: /init was not called");

        var received = 0;
        var typeRune = Translator.TypeFrom(request.Runes.First(), _speech.Alphabet);

        switch (typeRune)
        {
            case TypeCombine.Direct:
                _speech.PushDirect(Translator.DirectUnrune(string.Join("", _speech.DirectRunes), _speech.RuneAlphabet, _speech.RuneSize + 1, _speech.SymbolsOf(TypeCombine.Direct)));
                _speech.PushDirectRunes(request.Runes);

                received = _speech.Accept(Translator.DirectUnrune(string.Join("", _speech.DirectRunes), _speech.RuneAlphabet, _speech.RuneSize + 1, _speech.SymbolsOf(TypeCombine.Direct)));

                _logger.LogInformation($"combine: runes the {request.Runes} directly accepted");

                break;

            case TypeCombine.Fragmentate:
                received = _speech.Accept(request.Runes);

                _logger.LogInformation($"combine: runes digit {received} accepted");

                break;
           
            default: throw new ArgumentOutOfRangeException(nameof(typeRune), request, "Unexpected TypeCombine value");
        }

        return Task.FromResult(new CombineResponse { Received = received });
    }
}