using System.Data.Common;

using MediatR;
using Microsoft.EntityFrameworkCore;

using CombineQueries.Api.Services.Speech;
using CombineQueries.Api.Services.Forwarder;
using CombineQueries.Domain.Aggregates.Translator;
using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Tail;

// Единственный репозиторий — ITranslatorRepo: словарь и хайперы это части агрегата Translator.
public class TailHandler(ILogger<TailHandler> logger, IForward forwarder, ISpeech speech, ITranslatorRepo translatorRepo)
    : IRequestHandler<TailRequest, TailResponse>
{
    public async Task<TailResponse> Handle(TailRequest request, CancellationToken cancellationToken)
    {
        if (speech.Alphabet is null || speech.RuneAlphabet is null) throw new Exception("CRIT: /connect was not called");

        // Подпись сверяется ДО сборки и до форварда: чужой хвост не должен увести наружу URL,
        // собранный из чужих же чанков. Попытка ровно одна - дальше приём валится до connect.
        // Выключение - на клиенте: SignValues=1 делает подпись единственной и сверку тривиальной.
        if (request.Type == TypeQuery.Fragmentate && !speech.CheckSign(request.Sign))
        {
            speech.Fault($"tail sign {request.Sign} rejected");

            logger.LogWarning("tail: sign {Sign} rejected, stream dropped until connect", request.Sign);

            throw new Exception("auth error: tail sign rejected");
        }

        if (request.Type != TypeQuery.Fragmentate && request.Type != TypeQuery.Direct) throw new ArgumentOutOfRangeException(nameof(request), request.Type, "Unexpected TypeCombine value");

        string tail = Translator.TrimPad(request.Type == TypeQuery.Direct
            ? Translator.DirectUnrune(request.Runes, speech.RuneAlphabet, speech.Alphabet, speech.RuneSize)
            : Translator.FragmentateUnrune(request.Runes, speech.RuneAlphabet, speech.Alphabet, speech.RuneSize, speech.SymbolsOf(request.Type)), speech.RuneSize);

        var assembled = speech.Close(tail, request.Type);

        if (string.IsNullOrEmpty(assembled.Text)) throw new Exception("domain error: nothing was assembled");

        string url = speech.Scheme + "://" + assembled.Text;

        logger.LogInformation("tail: assembled {Runes} runes + {Chars} chars in {ElapsedMs} ms -> {Url}", assembled.Runes, tail.Length, assembled.ElapsedMs, url);

        var forwarded = await forwarder.GetAsync(url, cancellationToken);

        int handle = speech.Intern(url, assembled.ElapsedMs + forwarded.ElapsedMs);

        // Учим DF из собранного payload'а (assembled.Text - без схемы, ровно то, что токенизирует клиент).
        var learned = speech.LearnFrom(assembled.Text);

        logger.LogInformation("tail: first send took {TotalMs} ms total ({Requests} requests), handle {Handle}, +{Learned} fragments",
            assembled.ElapsedMs + forwarded.ElapsedMs, assembled.Runes + 1, handle, learned.Addressable.Count);

        // Финитные адреса (L2+L3) кончились: строки сохраняются с Level=Infinite, но клиенту не едут,
        // поэтому этот query уйдёт по ним буквами (direct-фоллбэк).
        if (learned.Overflowed.Count > 0)
            logger.LogWarning("tail: (not enough addresses) +{Overflowed} fragments stored as Infinite, direct for this query", learned.Overflowed.Count);

        await Persist(url, handle, learned, cancellationToken);

        return new TailResponse
        {
            Runes = assembled.Runes,
            ForwardedUrl = url,
            Response = forwarded.Body,
            Handle = handle,
            AssemblyMs = assembled.ElapsedMs,
            ForwardMs = forwarded.ElapsedMs,
            Fragments = learned.Addressable
        };
    }

    // Персист того, что нажили за этот запрос: новый хайпер + выученные фрагменты.
    // По возможности, как и в connect: рантайм живёт в памяти, отказ БД не имеет права ронять ответ -
    // клиент уже получил и URL, и тело.
    private async Task Persist(string url, int handle, LearnResult learned, CancellationToken cancellationToken)
    {
        try
        {
            var translator = await translatorRepo.GetByAlphabetAsync(speech.Alphabet!);

            // Транслятора нет - значит connect шёл конфиг-фолбэком, без БД. Сохранять некуда.
            if (translator is null) return;

            translator.Remember(handle, url);

            // Уровень Learn считает сам по адресу, так что обе группы кладутся одинаково: те, чей
            // адрес перевалил за потолок, получат Level=Infinite.
            foreach (var seed in learned.Addressable) translator.Learn(seed.Id, seed.Text, speech.DfaSize, speech.PageCount);

            foreach (var seed in learned.Overflowed) translator.Learn(seed.Id, seed.Text, speech.DfaSize, speech.PageCount);

            // Цепь Infinite пересшиваем, только если в неё реально что-то добавилось.
            if (learned.Overflowed.Count > 0) translator.ChainInfinite(speech.DfaSize * speech.PageCount);

            await translatorRepo.UnitOfWork.SaveEntitiesAsync(cancellationToken);
        }
        // Только отказ БД, как и в connect: клиент уже получил URL и тело, ронять ответ нельзя.
        // Ошибки кода при этом обязаны падать, а не маскироваться под «база недоступна».
        catch (Exception ex) when (ex is DbException or DbUpdateException)
        {
            logger.LogWarning("tail: persistence unavailable, memory only ({Kind}: {Message})", ex.GetType().Name, ex.Message);
        }
    }
}
