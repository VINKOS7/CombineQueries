using System.Data.Common;

using MediatR;
using Microsoft.EntityFrameworkCore;

using CombineQueries.Api.Services.Speech;
using CombineQueries.Domain.Aggregates.Account;
using CombineQueries.Domain.Aggregates.Translator;

namespace CombineQueries.Api.Controllers.Accounts.Handlers.Connect;

public class ConnectHandler : IRequestHandler<ConnectRequest, ConnectResponse>
{
    private readonly ILogger<ConnectHandler> _logger;
    private readonly ISpeech _speech;
    private readonly IAccountRepo _accountRepo;
    private readonly IConfiguration _configuration;
    private readonly ITranslatorRepo _translatorRepo;
    private readonly IWebHostEnvironment _environment;

    public ConnectHandler(IAccountRepo accountRepo, ITranslatorRepo translatorRepo, IConfiguration configuration, ILogger<ConnectHandler> logger, ISpeech speech, IWebHostEnvironment environment)
    {
        _logger = logger;
        _speech = speech;
        _accountRepo = accountRepo;
        _environment = environment;
        _configuration = configuration;
        _translatorRepo = translatorRepo;
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
                Hypers = request.Persist
            });

            _logger.LogInformation($"connect: alphabet {request.Alphabet.Length} chars, runeSize={runeSize}, scheme={request.Scheme}, dfaSize={dfaSize}, pageCount={pageCount}");

            if (account is not null)
            {
                if (again) account.Remember(request.Alphabet, request.baseForwardUrl);
                else account.Init(request.Alphabet, request.baseForwardUrl);

                await _accountRepo.UnitOfWork.SaveEntitiesAsync(cancellationToken);

                var translator = await TranslatorOf(request, cancellationToken);

                // Сброс идёт ДО заливки: иначе Warm тут же вернул бы забытое обратно из персиста.
                await ForgetHypers(request, translator, cancellationToken);

                Warm(translator);
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

    private async Task<Translator?> TranslatorOf(ConnectRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var translator = await _translatorRepo.GetByAlphabetAsync(request.Alphabet);

            if (translator is not null) return translator;

            translator = Translator.From(new InitCommand<char>
            {
                Runes = Translator.ATRFrom(request.Alphabet),
                BaseForwardUrl = request.baseForwardUrl,
                Alphabet = request.Alphabet
            });

            await _translatorRepo.AddAsync(translator);
            await _translatorRepo.UnitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("connect: new Translator persisted, ID={Id}", translator.Id);

            return translator;
        }
        // Только отказ БД. Ошибки кода обязаны падать, а не притворяться «базы нет».
        catch (Exception ex) when (ex is DbException or DbUpdateException)
        {
            _logger.LogWarning("connect: persistence unavailable, memory only ({Kind}: {Message})", ex.GetType().Name, ex.Message);

            return null;
        }
    }

    // Забывает хайперы и в рантайме, и в персисте - иначе они вернулись бы на ближайшем connect.
    // Только Development: на релизе это стирало бы то, что накопил живой мир.
    private async Task ForgetHypers(ConnectRequest request, Translator? translator, CancellationToken cancellationToken)
    {
        if (!request.ResetHypers || !_environment.IsDevelopment()) return;

        int forgotten = _speech.HyperUrls.Count + (translator?.Hypers.Count ?? 0);

        _speech.ForgetHypers();

        // Цепочки НЕ трогаем: в dev в БД лежит ровно одна, посеянная миграцией, и она должна
        // пережить сброс - иначе прыгать станет не по чему. Накопленное этим прогоном живёт в
        // памяти, его ForgetHypers уже стёр.
        if (translator is not null && translator.Hypers.Count > 0)
        {
            translator.Hypers.Clear();

            await _translatorRepo.UnitOfWork.SaveEntitiesAsync(cancellationToken);
        }

        _logger.LogInformation("connect: {Forgotten} hypers forgotten (dev reset)", forgotten);
    }

    // Тёплый словарь из персиста в рантайм. Адрес и handle - это индексы, поэтому строго по
    // возрастанию, дырки Restore добьёт сам.
    private void Warm(Translator? translator)
    {
        if (translator is null) return;

        var fragments = new List<FragmentSeed>(translator.VirtualFragments.Count);

        foreach (var fragment in translator.VirtualFragments) fragments.Add(new FragmentSeed(fragment.Id, fragment.Text));

        fragments.Sort((a, b) => a.Id.CompareTo(b.Id));

        var hypers = new List<HyperSeed>(translator.Hypers.Count);

        foreach (var hyper in translator.Hypers) hypers.Add(new HyperSeed(hyper.Id, hyper.Url));

        hypers.Sort((a, b) => a.Handle.CompareTo(b.Handle));

        // Дерево цепочек из персиста: номера узлов сохраняются, иначе выданные прыжки протухнут.
        // Поднимаем ВСЕГДА - hypers=off гасит появление новых цепочек, а не чтение накопленных.
        var chains = new List<(int, int?, string, string?)>(translator.Chains.Count);

        foreach (var chain in translator.Chains) chains.Add((chain.Id, chain.ParentId, chain.Step, chain.Url));

        _speech.RestoreChains(chains);

        _speech.Restore(fragments, hypers);

        _logger.LogInformation("connect: restored {Fragments} fragments, {Hypers} hypers, {Chains} chain nodes", fragments.Count, hypers.Count, chains.Count);
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
