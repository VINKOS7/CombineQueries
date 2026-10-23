using System.Diagnostics;

using Microsoft.Extensions.Logging;

namespace CombineQueries.Infra.Services.Speak;

public class Speak : ISpeak
{
    private readonly ILogger<Speak> _logger;
    private readonly HttpClient _httpClient;

    public Speak(ILogger<Speak> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task<ForwardResult> GetAsync(string url, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();

        // Ждём целевой API ровно timeout, а не сколько позволит HttpClient: потолок задаёт клиент на connect.
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        limit.CancelAfter(timeout);

        try
        {
            var response = await _httpClient.GetAsync(url, limit.Token);

            string body = await response.Content.ReadAsStringAsync(limit.Token);

            watch.Stop();

            int status = (int)response.StatusCode;

            _logger.LogInformation($"forward: '{url}' -> {status}, {watch.ElapsedMilliseconds} ms, {body.Length} chars");

            if (!response.IsSuccessStatusCode) _logger.LogWarning($"forward: target returned {status}");

            return new ForwardResult(true, status, body, watch.ElapsedMilliseconds, null);
        }
        catch (Exception ex)
        {
            watch.Stop();

            // Не дождались целевого API - отдельной строкой: клиенту это не сбой протокола, а долгий чужой сайт.
            string error = limit.IsCancellationRequested && !cancellationToken.IsCancellationRequested
                ? $"timeout: target did not answer in {timeout.TotalSeconds:0} s"
                : ex.Message;

            _logger.LogError($"forward: request to '{url}' failed after {watch.ElapsedMilliseconds} ms: {error}");

            return new ForwardResult(false, 0, "", watch.ElapsedMilliseconds, error);
        }
    }
}
