using System.Data.Common;

using MediatR;
using Microsoft.EntityFrameworkCore;

using CombineQueries.Api.Services.Speech;
using CombineQueries.Domain.Aggregates.Account;
using CombineQueries.Domain.Aggregates.Translator;

namespace CombineQueries.Api.Controllers.Accounts.Handlers.Connect;

// Свой агрегат тут ровно один - Account, и репозиторий тоже один. Всё, что касается Translator,
// уехало в AccountConnectedHandler и приходит туда доменным событием: аккаунт подключился -
// обработчик обеспечил словарь. Событие синхронное, поэтому ответ строится уже по прогретому.
public class ConnectHandler : IRequestHandler<ConnectRequest, ConnectResponse>
{
    private readonly ILogger<ConnectHandler> _logger;
    private readonly ISpeech _speech;
    private readonly IAccountRepo _accountRepo;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public ConnectHandler(IAccountRepo accountRepo, IConfiguration configuration, ILogger<ConnectHandler> logger, ISpeech speech, IWebHostEnvironment environment)
    {
        _logger = logger;
        _speech = speech;
        _accountRepo = accountRepo;
        _environment = environment;
        _configuration = configuration;
    }

    public async Task<ConnectResponse> Handle(ConnectRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var account = await Authorize(request.Token);

            int runeSize = request.RuneSize;

            if (runeSize < 2) throw new Exception($"domain error: runeSize={runeSize}, must be >= 2");

            if (request.Scheme != "http" && request.Scheme != "https") throw new Exception($"domain error: scheme={request.Scheme}, must be http or https");

            bool again = _speech.Alphabet is not null;

            var (dfaSize, pageCount) = SizesOf(request);

            _speech.SetContext(new SetContextCommand<char>
            {
                Alphabet = request.Alphabet,
                RuneSize = runeSize,
                Scheme = request.Scheme,
                DfaSize = dfaSize,
                PageCount = pageCount,
                HopCount = request.HopCount,
                Hypers = request.Persist,
                BaseForwardUrl = request.baseForwardUrl,
                ResetHypers = request.ResetHypers
            });

            _logger.LogInformation($"connect: alphabet {request.Alphabet.Length} chars, runeSize={runeSize}, scheme={request.Scheme}, dfaSize={dfaSize}, pageCount={pageCount}");

            if (account is not null)
            {
                if (again) account.Remember();
                else account.Init();

                // Тут и происходит работа с чужим агрегатом: SaveEntitiesAsync диспатчит
                // AccountConnected и ДОЖИДАЕТСЯ обработчика, поэтому дальше словарь уже тёплый.
                await _accountRepo.UnitOfWork.SaveEntitiesAsync(cancellationToken);
            }

            return new()
            {
                ShortDomain = "http://v.ro",
                RuneSize = runeSize,
                Scheme = request.Scheme,
                DfaSize = _speech.DfaSize,
                Signs = _speech.Signs,
                Jumps = JumpsOf(),
                Roots = Translator.Fragments,
                Hypers = Seed(_speech.HyperUrls, (i, u) => new HyperSeed(i, u), SeedLimit),
                Fragments = Seed(_speech.FragmentTexts, (i, t) => new FragmentSeed(i, t), Math.Min(SeedLimit, Reach(request)))
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Message);

            throw;
        }
    }

    private (int DfaSize, int PageCount) SizesOf(ConnectRequest request)
    {
        if (!_environment.IsDevelopment()) return (request.DfaSize, request.PageCount);

        int dfaSize = _configuration.GetValue("Dev:DfaSize", 16);

        if (dfaSize <= 0) return (request.DfaSize, request.PageCount);

        int pageCount = _configuration.GetValue("Dev:PageCount", 8);

        _logger.LogWarning("connect: dev sizes {Dfa}x{Pages} instead of {ClientDfa}x{ClientPages} - dictionary capped at {Capacity} to reach Infinite",
            dfaSize, pageCount, request.DfaSize, request.PageCount, dfaSize * pageCount);

        return (dfaSize, pageCount);
    }

    private int SeedLimit
    {
        get
        {
            int limit = _configuration.GetValue("Init:SeedLimit", 512);

            return limit > 0 ? limit : int.MaxValue;
        }
    }

    // Хайперы для клиента: только листы, плоским списком.
    private List<JumpSeed> JumpsOf()
    {
        var jumps = new List<JumpSeed>();

        foreach (var (url, jump) in _speech.ChainLeaves()) jumps.Add(new JumpSeed(url, jump));

        return jumps;
    }

    // Докуда клиенту вообще есть смысл слать словарь. Адрес это индекс, поэтому «не слать Infinite»
    // = не слать дальше потолка L3. С флагом - без ограничения, пусть сам решает, что удержит.
    private int Reach(ConnectRequest request) =>
        request.RememberInfinite ? int.MaxValue : _speech.DfaSize * _speech.PageCount;

    // Индекс списка = id/handle элемента, поэтому режем только с конца.
    private static List<TSeed> Seed<TSeed>(IReadOnlyList<string> source, Func<int, string, TSeed> make, int limit)
    {
        int count = limit > 0 && limit < source.Count ? limit : source.Count;

        var seed = new List<TSeed>(count);

        for (int i = 0; i < count; i++) seed.Add(make(i, source[i]));

        return seed;
    }

    // Возвращает tracked Account (чтобы поднять событие и сохранить) либо null при конфиг-фолбэке
    // (БД недоступна). Бросает при отказе авторизации.
    private async Task<Account?> Authorize(string token)
    {
        if (!Account.IsToken(token)) throw new Exception("auth error: token rejected");

        Account? account;

        try
        {
            account = await _accountRepo.GetByTokenAsync(token);
        }
        catch (Exception ex)
        {
            bool configured = token == _configuration["Auth:Token"];

            _logger.LogWarning("connect: accounts unavailable, configured token {Verdict} ({Kind}: {Message})", configured ? "accepted" : "rejected", ex.GetType().Name, ex.Message);

            if (configured) return null;

            throw new Exception("auth error: token rejected");
        }

        if (account is null) throw new Exception("auth error: token rejected");

        return account;
    }
}
