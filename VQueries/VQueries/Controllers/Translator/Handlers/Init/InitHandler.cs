using MediatR;

using CombineQueries.Api.Services.AFST;
using CombineQueries.Domain.Aggregates.Translator;

namespace CombineQueries.Api.Controllers.Translator.Handlers.Init;

public class InitHandler : IRequestHandler<InitRequest, InitResponse>
{
    private readonly ILogger<InitHandler> _logger;
    private readonly ITranslatorRepo _translatorRepo;
    private readonly IAFST _aFST;

    public InitHandler(ITranslatorRepo translatorRepo, ILogger<InitHandler> logger, IAFST aFST)
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

            // ОДНО дерево на всё: и в AFST, и в персистируемый Translator. Раньше тут ATRFrom
            // звался трижды и получались три несвязанных дерева.
            var runes = Domain.Aggregates.Translator.Translator.ATRFrom(request.Alphabet);

            _aFST.SetContext(new SetContextCommand<char>
            {
                Alphabet = request.Alphabet,
                ArenaTreeContext = runes,
                RuneSize = runeSize
            });

            _logger.LogInformation($"init: alphabet {request.Alphabet.Length} chars, runeSize={runeSize}");

            await TryPersist(runes, request, cancellationToken);

            return new() { ShortDomain = "http://v.ro", RuneSize = runeSize };
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
