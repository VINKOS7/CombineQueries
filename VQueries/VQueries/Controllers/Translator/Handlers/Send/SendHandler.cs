using CombineQueries.Api.Services.AFST;
using CombineQueries.Domain.Aggregates.Translator;
using CombineQueries.Domain.Aggregates.Translator.types;
using MediatR;

namespace CombineQueries.Api.Controllers.TranslatorController.Handlers.Send;

public class SendHandler : IRequestHandler<SendRequest, SendResponse>
{
    private readonly ILogger<SendHandler> _logger;
    private readonly IAFST _afst;
    private readonly ITranslatorRepo _translatorRepo;

    private readonly HttpClient _httpClient;
    public SendHandler(ILogger<SendHandler> logger, HttpClient client, ITranslatorRepo translator, IAFST alphabetFST)
    {
        _logger = logger;
        _httpClient = client;
        _translatorRepo = translator;
        _afst = alphabetFST;
    }

    public async Task<SendResponse> Handle(SendRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (_afst.Alphabet is null || _afst.ArenaTreeContext is null) throw new Exception("CRIT: /init не вызван");

            var response = await _httpClient.GetAsync(string.Concat(_afst.UnrunedCombine) + UnrunedCombine(_afst.Alphabet, _afst.ArenaTreeContext, request.Runes));

            _afst.UnrunedCombine.Clear();

            _logger.LogInformation("attempt send merged CombineQuery: success");

            return new SendResponse { Response = await response.Content.ReadAsStringAsync() };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.ToString());
            throw;
        }
    }

    // wireValue = matchedNode.Id * (alphabet.Length+1) + symbolIndex — тот же трюк с флэттингом пары,
    // что и в самом начале (idx1*baseLen+idx2), только по оси "узел дерева" вместо "символ алфавита".
    // symbolIndex == alphabet.Length — сентинел "без расширения" (финальный хвост без роста).
    private static string UnrunedCombine(string alphabet, IArenaTreeRunes<char> runes, string wireRunes)
    {
        int wireValue = 0;
        foreach (var c in wireRunes)
        {
            int digit = alphabet.IndexOf(c);

            if (digit < 0) throw new Exception($"domain error: symbol '{c}' not in alphabet");
            
            wireValue = wireValue * alphabet.Length + digit;
        }

        int symbolSpan = alphabet.Length + 1;
        var node = runes.Get(wireValue / symbolSpan);
        int symbolIndex = wireValue % symbolSpan;

        var chars = new Stack<char>();
        var walk = node;

        while (walk.ParentId != -1)
        {
            chars.Push(walk.ParentSymbol!);
            walk = runes.Get(walk.ParentId);
        }

        string prefix = new string(chars.ToArray());

        if (symbolIndex < alphabet.Length)
        {
            char ext = alphabet[symbolIndex];
            runes.From(node, ext);
            return prefix;
        }

        return prefix;
    }
}