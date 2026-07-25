using MediatR;

using CombineQueries.Api.Services.AFST;

namespace CombineQueries.Api.Controllers.Translator.Handlers.Handle;

// Один запрос вместо всей цепочки: ссылка уже известна серверу по идентификатору.
public class HandleHandler : IRequestHandler<HandleRequest, HandleResponse>
{
    private readonly ILogger<HandleHandler> _logger;
    private readonly IAFST _afst;
    private readonly HttpClient _httpClient;

    public HandleHandler(ILogger<HandleHandler> logger, HttpClient client, IAFST afst)
    {
        _logger = logger;
        _httpClient = client;
        _afst = afst;
    }

    public async Task<HandleResponse> Handle(HandleRequest request, CancellationToken cancellationToken)
    {
        if (_afst.Alphabet is null) throw new Exception("CRIT: /init не вызван");

        string? url = _afst.Resolve(request.Value);

        if (url is null)
        {
            _logger.LogWarning($"handle: {request.Value} неизвестен - клиенту надо слать ссылку целиком");

            return new HandleResponse { Known = false };
        }

        _logger.LogInformation($"handle: {request.Value} -> '{url}'");

        string body = "";

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode) _logger.LogWarning($"forward: целевой ресурс ответил {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"forward: не удалось запросить '{url}': {ex.Message}");
        }

        return new HandleResponse { Known = true, ForwardedUrl = url, Response = body };
    }
}
