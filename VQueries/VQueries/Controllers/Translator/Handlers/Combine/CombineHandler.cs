using MediatR;

using CombineQueries.Api.Services.AFST;
using CombineQueries.Domain.Aggregates.Translator;
using System.Net;

namespace CombineQueries.Api.Controllers.TranslatorController.Handlers.CQMergeSend;

public class CombineHandler : IRequestHandler<CombineRequest, CombineResponse>
{
    private readonly ILogger<CombineHandler> _logger;
    private readonly IAFST _alphabetFST;
    private readonly ITranslatorRepo _translatorRepo; // не читывать - персист отложен, decode целиком идёт через _alphabetFST
    private readonly HttpClient _httpClient;

    public CombineHandler(ILogger<CombineHandler> logger, HttpClient client, ITranslatorRepo translator, IAFST alphabetFST)
    {
        _logger = logger;
        _httpClient = client;
        _translatorRepo = translator;
        _alphabetFST = alphabetFST;
    }

    public async Task<CombineResponse> Handle(CombineRequest request, CancellationToken cancellationToken)
    {// while in the simple merge mode
        try
        {
            if (_alphabetFST.Alphabet is null || _alphabetFST.ArenaTreeContext is null) throw new Exception("CRIT: /init не вызван");

            var translaor = _translatorRepo.GetByAlphabetAsync(_alphabetFST.Alphabet);

            char marker = request.Runes[^1];
            bool isSend = marker == _alphabetFST.Alphabet[^1];
            string? forwarded = null;

            if (isSend)
            {
                // var httpResponse = _httpClient.GetAsync(translaor.Result.Unrune(string.Concat(_alphabetFST.CombineRunes)));
                var httpResponse = _httpClient.GetAsync(translaor.Result.Unrune(string.Concat(_alphabetFST.UnrunedCombine)));

                if(httpResponse.Result.StatusCode == HttpStatusCode.Accepted)
                {
                    _alphabetFST.UnrunedCombine.Clear();
                    _alphabetFST.CombineRunes.Clear();

                    forwarded = await httpResponse.Result.Content.ReadAsStringAsync();

                    _logger.LogInformation("attempt send merged CombineQuery: success");
                }
                else _logger.LogWarning($"attempt send merged CombineQuery: reject: target resource have error with status code: {httpResponse.Result.StatusCode}");
            }
            else
            {
                _alphabetFST.CombineRunes.Add(request.Runes);
                _alphabetFST.UnrunedCombine.Add(translaor.Result.Unrune());
            }

            return new CombineResponse { Response = forwarded };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());

            throw;
        }
    }

    public static string Decompress(string input, string clientAlphabet)
    {
        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(clientAlphabet)) return "";

        int baseLen = clientAlphabet.Length;
        char[] res = new char[input.Length * 2];
        int resIdx = 0;

        foreach (char c in input)
        {
            int id = c; // обратный каст того (char)id, которым Compress/IntArrayToString паковали id в символ

            int idx1 = id / baseLen;
            int idx2 = id % baseLen;

            res[resIdx] = clientAlphabet[idx1];
            res[resIdx + 1] = clientAlphabet[idx2];
            resIdx += 2;
        }

        return new string(res);
    }
}
