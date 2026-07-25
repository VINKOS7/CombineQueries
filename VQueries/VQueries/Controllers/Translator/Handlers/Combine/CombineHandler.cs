using MediatR;

using CombineQueries.Api.Services.AFST;
using CombineQueries.Api.Controllers.Translator.Handlers.Merge;
using CombineQueries.Api.Controllers.Translator.Handlers.MergeSend;

namespace CombineQueries.Api.Controllers.Translator.Handlers.Combine;

// Точка входа плоского роута. Сам ничего не склеивает и не шлёт - только разбирает трейлинг-маркер
// и диспатчит, чтобы каждый хендлер тянул ровно свои зависимости (merge не таскает HttpClient).
//
// Маркер - последний символ рун, не часть данных:
//   Alphabet[^1] = mergesend, буфер закодирован простым кодеком
//   Alphabet[^2] = mergesend, буфер закодирован деревом
//   любой другой = merge (просто слепить; чем закодировано - выяснится на mergesend)
//
// Режим выбирает КЛИЕНТ: пока дерево холодное - простой кодек, как прогреется до >=2x плотности
// простого - переключается на дерево. Сервер не гадает, он подчиняется маркеру.
public class CombineHandler : IRequestHandler<CombineRequest, CombineResponse>
{
    private readonly IMediator _mediator;
    private readonly IAFST _alphabetFST;

    public CombineHandler(IMediator mediator, IAFST alphabetFST)
    {
        _mediator = mediator;
        _alphabetFST = alphabetFST;
    }

    public async Task<CombineResponse> Handle(CombineRequest request, CancellationToken cancellationToken)
    {
        if (_alphabetFST.Alphabet is null) throw new Exception("CRIT: /init не вызван");
        if (string.IsNullOrEmpty(request.Runes)) throw new Exception("domain error: пустые руны");

        string alphabet = _alphabetFST.Alphabet;

        char marker = request.Runes[^1];
        string payload = request.Runes[..^1];

        if (marker == alphabet[^1] || marker == alphabet[^2])
        {
            var mode = marker == alphabet[^1] ? CombineMode.Simple : CombineMode.Tree;

            var sent = await _mediator.Send(new MergeSendRequest { Runes = payload, Mode = mode }, cancellationToken);

            return new CombineResponse { ForwardedUrl = sent.ForwardedUrl, Response = sent.Response };
        }

        var merged = await _mediator.Send(new MergeRequest { Runes = payload }, cancellationToken);

        return new CombineResponse { Count = merged.Count };
    }
}
