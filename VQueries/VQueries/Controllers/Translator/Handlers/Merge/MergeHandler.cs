using MediatR;

using CombineQueries.Api.Services.AFST;

namespace CombineQueries.Api.Controllers.Translator.Handlers.Merge;

// МЕРДЖ = только слепить. Кладёт руны в буфер AFST и всё - ничего не разжимает и никуда не шлёт.
// Разжатие и форвардинг - дело MergeSendHandler, когда придёт запрос с send-маркером.
public class MergeHandler : IRequestHandler<MergeRequest, MergeResponse>
{
    private readonly ILogger<MergeHandler> _logger;
    private readonly IAFST _alphabetFST;

    public MergeHandler(ILogger<MergeHandler> logger, IAFST alphabetFST)
    {
        _logger = logger;
        _alphabetFST = alphabetFST;
    }

    public Task<MergeResponse> Handle(MergeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (_alphabetFST.Alphabet is null) throw new Exception("CRIT: /init не вызван");

            // не бросаем - последний кусок законно может быть короче, но рассинхрон ширины руны
            // ловится именно здесь, иначе он всплывёт только кривым URL на mergesend
            if (request.Runes.Length != _alphabetFST.RuneSize)
                _logger.LogWarning($"merge: ширина куска {request.Runes.Length} != договорённой runeSize={_alphabetFST.RuneSize}");

            _alphabetFST.CombineRunes.Add(request.Runes);

            _logger.LogInformation($"merge: склеено кусков = {_alphabetFST.CombineRunes.Count}, руны = '{request.Runes}'");

            return Task.FromResult(new MergeResponse { Count = _alphabetFST.CombineRunes.Count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());

            throw;
        }
    }
}
