using MediatR;

using CombineQueries.Api.Services.AFST;
using CombineQueries.Domain.Aggregates.Translator;

namespace CombineQueries.Api.Controllers.TranslatorController.Handlers.CQMergeSend;

public class MergeHandler : IRequestHandler<MergeRequest, MergeResponse>
{
    private readonly ILogger<MergeHandler> _logger;
    private readonly IAFST _alphabetFST;
    private readonly ITranslatorRepo _translatorRepo;

    public MergeHandler(ILogger<MergeHandler> logger, HttpClient client, ITranslatorRepo translator, IAFST alphabetFST)
    {
        _logger = logger;
        _translatorRepo = translator;
        _alphabetFST = alphabetFST;
    }

    public async Task<MergeResponse> Handle(MergeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var translator = _translatorRepo.GetByAlphabetAsync(_alphabetFST.Alphabet ?? throw new Exception("CRIT: rootAlphabet is null"));

            _alphabetFST.UnrunedCombine.Add((await translator).Unruned(request.Runes));

            _logger.LogInformation("attempt merge CombineQuery: success");

            return new MergeResponse() { delta = "mmaybe parts server context" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());

            throw;
        }
    }
}
