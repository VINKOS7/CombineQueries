namespace CombineQueries.Api.Services.Forwarder;

public record ForwardResult(bool Ok, int Status, string Body, long ElapsedMs, string? Error);

public interface IForwarder
{
    Task<ForwardResult> GetAsync(string url, CancellationToken cancellationToken);
}
