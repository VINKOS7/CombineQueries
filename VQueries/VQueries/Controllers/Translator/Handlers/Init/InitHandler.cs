using MediatR;

using CombineQueries.Api.Services.AFST;
using CombineQueries.Domain.Aggregates.Translator;
using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Controllers.TranslatorController.Handlers.Init;

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
            var id = _translatorRepo.GetIdByAlphabetAsync(request.Alphabet);

            var webAlphabet = Translator.ATRFrom(request.Alphabet);

            _aFST.SetContext(new SetContextCommand<char> () { Alphabet = request.Alphabet, ArenaTreeContext = Translator.ATRFrom(request.Alphabet) });

            if (await id != Guid.Empty) return new () { ShortDomain = "http://v.ro" };
            else
            {
                var translator = Translator.From(new InitCommand<char> { Runes = Translator.ATRFrom(request.Alphabet), BaseForwardUrl = request.baseForwardUrl, Alphabet = request.Alphabet });

                await _translatorRepo.AddAsync(translator);

                await _translatorRepo.UnitOfWork.SaveChangesAsync(cancellationToken);

                _logger.LogInformation($"attempt init alphabet - success: added new with ID: { translator.Id }");

                return new () { ShortDomain = "http://v.ro" };
            }
        }
        catch (Exception ex) 
        {
            _logger.LogError(ex.Message);

            throw;
        }
    }
}
