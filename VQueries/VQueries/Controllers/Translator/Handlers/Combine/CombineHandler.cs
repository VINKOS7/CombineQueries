using MediatR;

using CombineQueries.Api.Services.AFST;

namespace CombineQueries.Api.Controllers.Translator.Handlers.Combine;

// Принимает кусок и склеивает. Отдельного "отправляй" в протоколе больше нет: сколько кусков
// будет, объявлено заранее через /n, и как только набралось - здесь же идёт форвардинг.
// Собранная ссылка интернируется, и её идентификатор уезжает клиенту в ответе: со второго раза
// та же ссылка отправляется ОДНИМ запросом через /h.
public class CombineHandler : IRequestHandler<CombineRequest, CombineResponse>
{
    private readonly ILogger<CombineHandler> _logger;
    private readonly IAFST _afst;
    private readonly HttpClient _httpClient;

    public CombineHandler(ILogger<CombineHandler> logger, HttpClient client, IAFST afst)
    {
        _logger = logger;
        _httpClient = client;
        _afst = afst;
    }

    public async Task<CombineResponse> Handle(CombineRequest request, CancellationToken cancellationToken)
    {
        if (_afst.Alphabet is null) throw new Exception("CRIT: /init не вызван");
        if (string.IsNullOrEmpty(request.Runes)) throw new Exception("domain error: пустые руны");

        var chunk = _afst.Accept(request.Runes);

        if (!chunk.Complete)
        {
            _logger.LogInformation($"combine: {chunk.Received}/{chunk.Expected}");

            return new CombineResponse { Received = chunk.Received, Expected = chunk.Expected };
        }

        string url = chunk.Text!;
        int handle = _afst.Intern(url);

        _logger.LogInformation($"combine: собрано {chunk.Received} кусков -> '{url}', хэндл {handle}");

        string body = await Forward(url, cancellationToken);

        return new CombineResponse
        {
            Received = chunk.Received,
            Expected = chunk.Expected,
            Complete = true,
            ForwardedUrl = url,
            Response = body,
            Handle = handle
        };
    }

    private async Task<string> Forward(string url, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode) _logger.LogWarning($"forward: целевой ресурс ответил {response.StatusCode}");

            return body;
        }
        catch (Exception ex)
        {
            // Ссылку собрали верно, а форвардинг не удался - это разные беды. Хэндл уже выдан,
            // повторная попытка пойдёт одним запросом, поэтому сборку не отменяем.
            _logger.LogError($"forward: не удалось запросить '{url}': {ex.Message}");

            return "";
        }
    }
}
