namespace CombineQueries.Api.Services.Forwarder;

// Единственное место, где живёт исходящий HTTP. Раньше тот же try/catch был скопирован
// в CombineHandler и HandleHandler - две копии одной политики ошибок, расходящиеся при первой правке.
public class Forwarder : IForwarder
{
    private readonly ILogger<Forwarder> _logger;
    private readonly HttpClient _httpClient;

    public Forwarder(ILogger<Forwarder> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task<ForwardResult> GetAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);

            int status = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode) _logger.LogWarning($"forward: '{url}' ответил {status}");

            return new ForwardResult(true, status, body, null);
        }
        catch (Exception ex)
        {
            // Ссылку собрали верно, а сходить не смогли - это разные беды, и хэндл уже выдан:
            // повторная попытка пойдёт одним запросом, поэтому сборку не отменяем.
            _logger.LogError($"forward: не удалось запросить '{url}': {ex.Message}");

            return new ForwardResult(false, 0, "", ex.Message);
        }
    }
}
