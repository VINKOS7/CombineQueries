using MediatR;

using CombineQueries.Api.Services.AFST;
using CombineQueries.Domain.Aggregates.Translator;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Init;

public class InitHandler : IRequestHandler<InitRequest, InitResponse>
{
    private readonly ILogger<InitHandler> _logger;
    private readonly ITranslatorRepo _translatorRepo;
    private readonly ISpeech _aFST;

    public InitHandler(ITranslatorRepo translatorRepo, ILogger<InitHandler> logger, ISpeech aFST)
    {
        _logger = logger;
        _translatorRepo = translatorRepo;
        _aFST = aFST;
    }

    public async Task<InitResponse> Handle(InitRequest request, CancellationToken cancellationToken)
    {
        try
        {
            int runeSize = request.RuneSize;

            if (runeSize < 2) throw new Exception($"domain error: runeSize={runeSize}, must be >= 2");

            if (request.Scheme != "http" && request.Scheme != "https")
                throw new Exception($"domain error: scheme={request.Scheme}, must be http or https");

            var runes = Domain.Aggregates.Translator.Translator.ATRFrom(request.Alphabet);

            _aFST.SetContext(new SetContextCommand<char>
            {
                Alphabet = request.Alphabet,
                RuneSize = runeSize,
                Scheme = request.Scheme
            });

            _logger.LogInformation($"init: alphabet {request.Alphabet.Length} chars, runeSize={runeSize}, scheme={request.Scheme}");

            await TryPersist(runes, request, cancellationToken);

            return new() { ShortDomain = "http://v.ro", RuneSize = runeSize, Scheme = request.Scheme };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Message);

            throw;
        }
    }

    private async Task TryPersist(Domain.Aggregates.Translator.types.IArenaTreeRunes<char> runes, InitRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (await _translatorRepo.GetIdByAlphabetAsync(request.Alphabet) != Guid.Empty) return;

            var translator = Domain.Aggregates.Translator.Translator.From(new InitCommand<char>
            {
                Runes = runes,
                BaseForwardUrl = request.baseForwardUrl,
                Alphabet = request.Alphabet
            });

            await _translatorRepo.AddAsync(translator);
            await _translatorRepo.UnitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation($"init: new Translator persisted, ID={translator.Id}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"init: persistence unavailable, running in memory only ({ex.GetType().Name}: {ex.Message})");
        }
    }
}
