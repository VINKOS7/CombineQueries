using MediatR;

using CombineQueries.Api.Services.Speech;
using CombineQueries.Domain.Aggregates.Translator;
using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Combine;

public class CombineHandler(ILogger<CombineHandler> logger, ISpeech speech) : IRequestHandler<CombineRequest, CombineResponse>
{
    public Task<CombineResponse> Handle(CombineRequest request, CancellationToken cancellationToken)
    {
        if (speech.Alphabet is null || speech.RuneAlphabet is null) throw new Exception("CombineHandler: /init was not called");

        if (string.IsNullOrEmpty(request.Runes)) throw new Exception("CombineHandler: empty runes");

        int symbols = speech.SymbolsOf(TypeQuery.Fragmentate);

        bool fragmentate = Translator.HasFragment(request.Runes, speech.RuneAlphabet, speech.Alphabet, speech.RuneSize, symbols);

        if (!fragmentate) speech.PushDirectRunes(request.Runes);

        int received = speech.Accept(request.Runes);

        logger.LogInformation("combine: rune {Received} accepted, {Kind}", received, fragmentate ? "with fragments" : "direct");

        return Task.FromResult(new CombineResponse { Received = received });
    }
}
