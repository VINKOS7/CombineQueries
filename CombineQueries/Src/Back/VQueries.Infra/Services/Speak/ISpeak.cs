namespace CombineQueries.Infra.Services.Speak;

public record ForwardResult(bool Ok, int Status, string Body, long ElapsedMs, string? Error);

public interface ISpeak
{
    // timeout - сколько ждать целевой API. Свой у каждого connect (maxTickTime), поэтому не в HttpClient.
    Task<ForwardResult> GetAsync(string url, TimeSpan timeout, CancellationToken cancellationToken);
}
