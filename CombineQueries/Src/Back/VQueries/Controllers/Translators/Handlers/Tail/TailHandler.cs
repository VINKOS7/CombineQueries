using System.Data.Common;

using MediatR;

using CombineQueries.Api.Services.Speech;
using CombineQueries.Api.Services.Outbox;
using CombineQueries.Api.Services.Persist;

using CombineQueries.Infra.Services.Speak;

using CombineQueries.Domain.Aggregates.Translator;
using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Tail;

public class TailHandler(ILogger<TailHandler> logger, IOutbox outbox, ISpeak speak, ISpeech speech, ITranslatorRepo translatorRepo)
    : IRequestHandler<TailRequest, TailResponse>
{
    public async Task<TailResponse> Handle(TailRequest request, CancellationToken cancellationToken)
    {
        if (speech.Alphabet is null || speech.RuneAlphabet is null) throw new Exception("CRIT: /connect was not called");

        bool single = request.Rune.Length > 0 || request.Pair;

        if (!single && request.Type == TypeQuery.Fragmentate && !speech.CheckSign(request.Sign))
        {
            logger.LogWarning("tail: sign {Sign} rejected, not in the expected parts", request.Sign);

            throw new Exception("auth error: tail sign rejected");
        }

        if (request.Type != TypeQuery.Fragmentate && request.Type != TypeQuery.Direct) throw new ArgumentOutOfRangeException(nameof(request), request.Type, "Unexpected TypeCombine value");

        if (!request.Merge) speech.Drop();

        if (request.Fragment >= 0)
        {
            speech.SetFragmentPage(request.Fragment / speech.DfaSize);
            speech.AcceptVirtualFragment(request.Fragment % speech.DfaSize);
        }

        if (request.Rune.Length > 0) speech.Accept(request.Rune);

        string tail = request.Runes.Length == 0
            ? ""
            : Translator.TrimPad(request.Type == TypeQuery.Direct
                ? Translator.DirectUnrune(request.Runes, speech.RuneAlphabet, speech.Alphabet, speech.TunnelsCount)
                : Translator.FragmentateUnrune(request.Runes, speech.RuneAlphabet, speech.Alphabet, speech.TunnelsCount, speech.SymbolsOf(request.Type)), speech.TunnelsCount);

        var assembled = speech.Close(tail, request.Type);

        if (string.IsNullOrEmpty(assembled.Text)) throw new Exception("domain error: nothing was assembled");

        string url = speech.Scheme + "://" + assembled.Text;

        logger.LogInformation("tail: assembled {Runes} pieces ({Chunks} runes, L2 {L2}, L3 {L3}, inf {Inf}) + {Chars} chars in {ElapsedMs} ms -> {Url}",
            assembled.Runes, assembled.Chunks, assembled.L2, assembled.L3, assembled.Infinite, tail.Length, assembled.ElapsedMs, url);

        IReadOnlyList<Delivery> ready;

        if (single)
        {
            var spoken = await speak.GetAsync(url, TimeSpan.FromSeconds(speech.MaxTickTime), cancellationToken);

            ready = [.. outbox.Take(speech.Stream), new Delivery(url, spoken.Body, spoken.ElapsedMs, spoken.Ok ? null : spoken.Error ?? "failed")];
        }
        else
        {
            outbox.Fetch(url, speech.Stream);

            ready = outbox.Take(speech.Stream);
        }

        int handle = speech.Intern(url, assembled.ElapsedMs);

        var learned = speech.LearnFrom(assembled.Text);

        logger.LogInformation("tail: tree now {Chains} chains in {Nodes} nodes, deepest {Deep} | leaf {Leaf}, prefix {Prefix} (shared {Shared})",
            speech.TreeChains, speech.TreeNodes, speech.TreeDeepest, speech.LastLeaf, speech.LastPrefix, speech.LastShared);

        logger.LogInformation("tail: assembled in {TotalMs} ms ({Requests} requests), handle {Handle}, +{Learned} fragments, {Ready} ready now, {Pending} in flight",
            assembled.ElapsedMs, assembled.Runes + 1, handle, learned.Addressable.Count, ready.Count, outbox.Pending(speech.Stream));

        if (learned.Overflowed.Count > 0)
            logger.LogWarning("tail: (not enough addresses) +{Overflowed} fragments stored as Infinite, direct for this query", learned.Overflowed.Count);

        await Persist(url, handle, learned, cancellationToken);

        return new TailResponse
        {
            Signs = single ? null : speech.TakeFreshSigns(),
            Runes = assembled.Runes,
            ForwardedUrl = url,
            Ready = ready,
            Pending = outbox.Pending(speech.Stream),
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

    private async Task Persist (string url, int handle, LearnResult learned, CancellationToken cancellationToken)
    {
        try
        {
            var translator = await translatorRepo.GetByAlphabetAsync(speech.Alphabet!);

            if (translator is null) return;

            if (url.Length > speech.LengthBatch)
            {
                logger.LogWarning("tail: url is {Length} chars, over lengthBatch {LengthBatch} - hyper kept in memory only", url.Length, speech.LengthBatch);

                speech.TakeChains();
            }
            else if (speech.Hypers)
            {
                translator.Remember(handle, url);

                foreach (var (id, parentId, step, chainUrl) in speech.TakeChains()) translator.Grow(id, parentId, step, chainUrl);
            }
            else speech.TakeChains();

            foreach (var seed in learned.Addressable) translator.Learn(seed.Id, seed.Text, speech.DfaSize, speech.PageCount, seed.Base);

            foreach (var seed in learned.Overflowed) translator.Learn(seed.Id, seed.Text, speech.DfaSize, speech.PageCount);

            await translatorRepo.UnitOfWork.SaveEntitiesAsync(cancellationToken);
        }
        catch (Exception ex) when (PersistFailure.Unavailable(ex))
        {
            logger.LogWarning("tail: persistence unavailable, memory only ({Kind}: {Message})", ex.GetType().Name, ex.Message);
        }
    }
}
