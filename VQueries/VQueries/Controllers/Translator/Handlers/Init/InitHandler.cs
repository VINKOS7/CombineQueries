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

            // Любая ширина >= 2. Степень двойки не требуется: декод простого режима - склейка рун,
            // а не парная распаковка, так что нечётные ширины (3) работают штатно.
            if (runeSize < 2) throw new Exception($"domain error: runeSize={runeSize} - должен быть >= 2");

            var id = _translatorRepo.GetIdByAlphabetAsync(request.Alphabet);

            // ОДНО дерево на всё: и в AFST, и в персистируемый Translator. Раньше тут ATRFrom
            // звался трижды и получались три несвязанных дерева.
            var runes = Domain.Aggregates.Translator.Translator.ATRFrom(request.Alphabet);

            _aFST.SetContext(new SetContextCommand<char>
            {
                Alphabet = request.Alphabet,
                ArenaTreeContext = runes,
                RuneSize = runeSize
            });

            _logger.LogInformation($"init: алфавит {request.Alphabet.Length} симв, runeSize={runeSize}");

            if (await id == Guid.Empty)
            {
                var translator = Domain.Aggregates.Translator.Translator.From(new InitCommand<char>
                {
                    Runes = runes,
                    BaseForwardUrl = request.baseForwardUrl,
                    Alphabet = request.Alphabet
                });

                await _translatorRepo.AddAsync(translator);
                await _translatorRepo.UnitOfWork.SaveChangesAsync(cancellationToken);

                _logger.LogInformation($"init: добавлен новый Translator ID={translator.Id}");
            }

            return new() { ShortDomain = "http://v.ro", RuneSize = runeSize };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Message);

            throw;
        }
    }
}
