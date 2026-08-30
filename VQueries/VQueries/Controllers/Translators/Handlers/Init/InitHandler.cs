using MediatR;

using CombineQueries.Api.Services.Speech;
using CombineQueries.Domain.Aggregates.Translator;
using CombineQueries.Domain.Aggregates.Account;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Init;

public class InitHandler : IRequestHandler<InitRequest, InitResponse>
{
    private readonly ILogger<InitHandler> _logger;
    private readonly ITranslatorRepo _translatorRepo;
    private readonly IAccountRepo _accountRepo;
    private readonly IConfiguration _configuration;
    private readonly ISpeech _aFST;

    public InitHandler(ITranslatorRepo translatorRepo, IAccountRepo accountRepo, IConfiguration configuration, ILogger<InitHandler> logger, ISpeech aFST)
    {
        _logger = logger;
        _translatorRepo = translatorRepo;
        _accountRepo = accountRepo;
        _configuration = configuration;
        _aFST = aFST;
    }

    public async Task<InitResponse> Handle(InitRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (!await Allowed(request.Token)) throw new Exception("auth error: token rejected");

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

    private async Task<bool> Allowed(string token)
    {
        if (!Domain.Aggregates.Account.Account.IsToken(token)) return false;

        try
        {
            if (await _accountRepo.GetIdByTokenAsync(token) != Guid.Empty) return true;
        }
        catch (Exception ex)
        {
            bool configured = token == _configuration["Auth:Token"];

            _logger.LogWarning("init: accounts unavailable, configured token {Verdict} ({Kind}: {Message})",
                configured ? "accepted" : "rejected", ex.GetType().Name, ex.Message);

            return configured;
        }

        return false;
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
