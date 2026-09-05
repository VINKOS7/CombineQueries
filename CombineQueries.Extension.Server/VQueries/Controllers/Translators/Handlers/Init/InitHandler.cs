using MediatR;

using CombineQueries.Api.Services.Speech;
using CombineQueries.Domain.Aggregates.Account;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Init;

public class InitHandler : IRequestHandler<InitRequest, InitResponse>
{
    private readonly ILogger<InitHandler> _logger;
    private readonly IAccountRepo _accountRepo;
    private readonly IConfiguration _configuration;
    private readonly ISpeech _aFST;

    // Один репозиторий — IAccountRepo (авторизация синхронна, это гейт). Персист Translator
    // вынесен в ConnectedHandler через доменное событие: правило «один хендлер — один репо».
    public InitHandler(IAccountRepo accountRepo, IConfiguration configuration, ILogger<InitHandler> logger, ISpeech aFST)
    {
        _logger = logger;
        _accountRepo = accountRepo;
        _configuration = configuration;
        _aFST = aFST;
    }

    public async Task<InitResponse> Handle(InitRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var account = await Authorize(request.Token);

            int runeSize = request.RuneSize;

            if (runeSize < 2) throw new Exception($"domain error: runeSize={runeSize}, must be >= 2");

            if (request.Scheme != "http" && request.Scheme != "https")
                throw new Exception($"domain error: scheme={request.Scheme}, must be http or https");

            _aFST.SetContext(new SetContextCommand<char>
            {
                Alphabet = request.Alphabet,
                RuneSize = runeSize,
                Scheme = request.Scheme,
                DfaSize = request.DfaSize,
                PageCount = request.PageCount
            });

            _logger.LogInformation($"init: alphabet {request.Alphabet.Length} chars, runeSize={runeSize}, scheme={request.Scheme}, dfaSize={request.DfaSize}, pageCount={request.PageCount}");

            // Кросс-агрегатная связь через доменное событие: Account поднимает Connected, Dotseed
            // диспатчит на SaveEntitiesAsync -> ConnectedHandler (со своим ITranslatorRepo) обеспечивает
            // Translator. Без Account (конфиг-фолбэк / нет БД) - протокол просто в памяти.
            if (account is not null)
            {
                account.Connect(request.Alphabet, request.baseForwardUrl);

                await _accountRepo.UnitOfWork.SaveEntitiesAsync(cancellationToken);
            }

            // connect-сид: тёплый словарь мастера (корни + хайперы + фрагменты) + эхо структуры.
            return new()
            {
                ShortDomain = "http://v.ro",
                RuneSize = runeSize,
                Scheme = request.Scheme,
                DfaSize = _aFST.DfaSize,
                Roots = Domain.Aggregates.Translator.Translator.Fragments,
                Hypers = Seed(_aFST.HyperUrls, (i, u) => new HyperSeed(i, u)),
                Fragments = Seed(_aFST.FragmentTexts, (i, t) => new FragmentSeed(i, t))
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Message);

            throw;
        }
    }

    // Индекс списка = id/handle элемента.
    private static List<TSeed> Seed<TSeed>(IReadOnlyList<string> source, Func<int, string, TSeed> make)
    {
        var seed = new List<TSeed>(source.Count);

        for (int i = 0; i < source.Count; i++) seed.Add(make(i, source[i]));

        return seed;
    }

    // Возвращает tracked Account (чтобы поднять событие и сохранить) либо null при конфиг-фолбэке
    // (БД недоступна). Бросает при отказе авторизации.
    private async Task<Domain.Aggregates.Account.Account?> Authorize(string token)
    {
        if (!Domain.Aggregates.Account.Account.IsToken(token)) throw new Exception("auth error: token rejected");

        Domain.Aggregates.Account.Account? account;

        try
        {
            account = await _accountRepo.GetByTokenAsync(token);
        }
        catch (Exception ex)
        {
            bool configured = token == _configuration["Auth:Token"];

            _logger.LogWarning("init: accounts unavailable, configured token {Verdict} ({Kind}: {Message})",
                configured ? "accepted" : "rejected", ex.GetType().Name, ex.Message);

            if (configured) return null;

            throw new Exception("auth error: token rejected");
        }

        if (account is null) throw new Exception("auth error: token rejected");

        return account;
    }
}
