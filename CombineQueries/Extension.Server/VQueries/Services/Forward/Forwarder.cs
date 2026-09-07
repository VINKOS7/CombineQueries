using System.Diagnostics;

namespace CombineQueries.Api.Services.Forwarder;

public class Forwarder : IForward
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

            _logger.LogError($"forward: request to '{url}' failed after {watch.ElapsedMilliseconds} ms: {ex.Message}");

            return new ForwardResult(false, 0, "", watch.ElapsedMilliseconds, ex.Message);
        }
    }
}
