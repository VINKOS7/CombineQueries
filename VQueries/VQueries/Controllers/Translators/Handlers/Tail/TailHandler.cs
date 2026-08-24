using MediatR;

using CombineQueries.Api.Services.Speech;
using CombineQueries.Api.Services.Forwarder;
using CombineQueries.Domain.Aggregates.Translator;
using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Tail;

public class TailHandler(ILogger<TailHandler> logger, IForward forwarder, ISpeech speech) : IRequestHandler<TailRequest, TailResponse>
{
    public async Task<TailResponse> Handle(TailRequest request, CancellationToken cancellationToken)
    {
        if (speech.Alphabet is null || speech.RuneAlphabet is null) throw new Exception("CRIT: /init was not called");

        if (request.Type != TypeCombine.Fragmentate && request.Type != TypeCombine.Direct)
            throw new ArgumentOutOfRangeException(nameof(request), request.Type, "Unexpected TypeCombine value");

        string tail = Translator.TrimPad(request.Type == TypeCombine.Direct
            ? Translator.DirectUnrune(request.Runes, speech.RuneAlphabet, speech.Alphabet, speech.RuneSize)
            : Translator.FragmentateUnrune(request.Runes, speech.RuneAlphabet, speech.Alphabet, speech.RuneSize, speech.SymbolsOf(request.Type)), speech.RuneSize);

        var assembled = speech.Close(tail, request.Type);

        if (string.IsNullOrEmpty(assembled.Text)) throw new Exception("domain error: nothing was assembled");

        string url = speech.Scheme + "://" + assembled.Text;

        logger.LogInformation("tail: assembled {Runes} runes + {Chars} chars in {ElapsedMs} ms -> {Url}",
            assembled.Runes, tail.Length, assembled.ElapsedMs, url);

        var forwarded = await forwarder.GetAsync(url, cancellationToken);

        int handle = speech.Intern(url, assembled.ElapsedMs + forwarded.ElapsedMs);

        logger.LogInformation("tail: first send took {TotalMs} ms total ({Requests} requests), handle {Handle}",
            assembled.ElapsedMs + forwarded.ElapsedMs, assembled.Runes + 1, handle);

        return new TailResponse
        {
            Runes = assembled.Runes,
            ForwardedUrl = url,
            Response = forwarded.Body,
            Handle = handle,
            AssemblyMs = assembled.ElapsedMs,
            ForwardMs = forwarded.ElapsedMs
        };
    }
}
