using System.Diagnostics;

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
        // Меряем и на успехе, и на провале: таймаут - тоже результат, и он самый долгий.
        var watch = Stopwatch.StartNew();

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);

            string body = await response.Content.ReadAsStringAsync(cancellationToken);

            watch.Stop();

            int status = (int)response.StatusCode;

            _logger.LogInformation($"forward: '{url}' -> {status}, {watch.ElapsedMilliseconds} ms, {body.Length} chars");

            if (!response.IsSuccessStatusCode) _logger.LogWarning($"forward: target returned {status}");

            return new ForwardResult(true, status, body, watch.ElapsedMilliseconds, null);
        }
        catch (Exception ex)
        {
            watch.Stop();

            // Ссылку собрали верно, а сходить не смогли - это разные беды, и хэндл уже выдан:
            // повторная попытка пойдёт одним запросом, поэтому сборку не отменяем.
            _logger.LogError($"forward: request to '{url}' failed after {watch.ElapsedMilliseconds} ms: {ex.Message}");

            return new ForwardResult(false, 0, "", watch.ElapsedMilliseconds, ex.Message);
        }
    }
}
