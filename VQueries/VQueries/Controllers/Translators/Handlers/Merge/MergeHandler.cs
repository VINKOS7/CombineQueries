using MediatR;

using CombineQueries.Api.Services.Speech;
using CombineQueries.Api.Controllers.Translators.Handlers.Combine;
using CombineQueries.Domain.Aggregates.Translator;
using CombineQueries.Domain.Aggregates.Translator.types;

namespace MergeQueries.Api.Controllers.Translators.Handlers.Merge;

public class MergeHandler(ILogger<MergeHandler> logger, ISpeech speech) : IRequestHandler<MergeRequest, MergeResponse>
{
    public Task<MergeResponse> Handle(MergeRequest request, CancellationToken cancellationToken)
    {
        if (speech.Alphabet is null || speech.RuneAlphabet is null) throw new Exception("MergeHandler: /init was not called");

        if (string.IsNullOrEmpty(request.Runes)) throw new Exception("MergeHandler: empty runes");

        int symbols = speech.SymbolsOf(TypeQuery.Fragmentate);

        bool fragmentate = Translator.HasFragment(request.Runes, speech.RuneAlphabet, speech.Alphabet, speech.RuneSize, symbols);

        if (!fragmentate) speech.PushDirectRunes(request.Runes);

        int received = speech.Accept(request.Runes);

        logger.LogInformation("Merge: rune {Received} accepted, {Kind}", received, fragmentate ? "with fragments" : "direct");

        return Task.FromResult(new MergeResponse { Received = received });
    }
}
