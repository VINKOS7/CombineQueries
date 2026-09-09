using System.Data.Common;

using MediatR;
using Microsoft.EntityFrameworkCore;

using CombineQueries.Api.Services.Persist;
using CombineQueries.Api.Services.Speech;
using CombineQueries.Api.Services.Outbox;
using CombineQueries.Domain.Aggregates.Translator;
using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Tail;

// Единственный репозиторий — ITranslatorRepo: словарь и хайперы это части агрегата Translator.
public class TailHandler(ILogger<TailHandler> logger, IOutbox outbox, ISpeech speech, ITranslatorRepo translatorRepo)
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

        logger.LogInformation("tail: assembled {Runes} pieces ({Chunks} runes, L2 {L2}, L3 {L3}, inf {Inf}) + {Chars} chars in {ElapsedMs} ms -> {Url}",
            assembled.Runes, assembled.Chunks, assembled.L2, assembled.L3, assembled.Infinite, tail.Length, assembled.ElapsedMs, url);

        // Наружу идём в фон: сборка закончена, а ждать чужой сервер клиенту незачем. Тело приедет
        // ДОЛГОМ - с этим же ответом, если успело, иначе со следующим запросом.
        outbox.Fetch(url);

        var ready = outbox.Take();

        int handle = speech.Intern(url, assembled.ElapsedMs);

        var learned = speech.LearnFrom(assembled.Text);

        logger.LogInformation("tail: tree now {Chains} chains in {Nodes} nodes, deepest {Deep} | leaf {Leaf}, prefix {Prefix} (shared {Shared})",
            speech.TreeChains, speech.TreeNodes, speech.TreeDeepest, speech.LastLeaf, speech.LastPrefix, speech.LastShared);

        logger.LogInformation("tail: assembled in {TotalMs} ms ({Requests} requests), handle {Handle}, +{Learned} fragments, {Ready} ready now, {Pending} in flight",
            assembled.ElapsedMs, assembled.Runes + 1, handle, learned.Addressable.Count, ready.Count, outbox.Pending);

        if (learned.Overflowed.Count > 0)
            logger.LogWarning("tail: (not enough addresses) +{Overflowed} fragments stored as Infinite, direct for this query", learned.Overflowed.Count);

        await Persist(url, handle, learned, cancellationToken);

        //много инфы для логов в дев
        return new TailResponse
        {
            Runes = assembled.Runes,
            ForwardedUrl = url,
            Ready = ready,
            Pending = outbox.Pending,
            Handle = handle,
            Leaf = speech.LastLeaf,
            Prefix = speech.LastPrefix,
            Shared = speech.LastShared,
            Chains = speech.TreeChains,
            Nodes = speech.TreeNodes,
            Chunks = assembled.Chunks,
            L2 = assembled.L2,
            L3 = assembled.L3,
            Infinite = assembled.Infinite,
            AssemblyMs = assembled.ElapsedMs,
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

            if (translator is null) return;

            // Хайперы - и плоский handle -> url, и узлы дерева - пишутся только при hypers=on.
            // С off накопленное читается, но не пополняется: очередь узлов забираем и выбрасываем,
            // иначе при включении в БД уехала бы вся история этого запуска разом.
            if (speech.Hypers)
            {
                translator.Remember(handle, url);

                foreach (var (id, parentId, step, chainUrl) in speech.TakeChains()) translator.Grow(id, parentId, step, chainUrl);
            }
            else speech.TakeChains();

            // адрес перевалил за потолок, получат Level=Infinite.
            foreach (var seed in learned.Addressable) translator.Learn(seed.Id, seed.Text, speech.DfaSize, speech.PageCount);
            
            //хм зачем второй раз, мб нужно разделение адресные, или бесконечные, кажется это связано с механизмом Займа
            foreach (var seed in learned.Overflowed) translator.Learn(seed.Id, seed.Text, speech.DfaSize, speech.PageCount);

            // Цепь Infinite пересшиваем, только если в неё реально что-то добавилось.
            if (learned.Overflowed.Count > 0) translator.ChainInfinite(speech.DfaSize * speech.PageCount);

            await translatorRepo.UnitOfWork.SaveEntitiesAsync(cancellationToken);
        }
        catch (Exception ex) when (PersistFailure.Unavailable(ex))
        {
            logger.LogWarning("tail: persistence unavailable, memory only ({Kind}: {Message})", ex.GetType().Name, ex.Message);
        }
    }
}
