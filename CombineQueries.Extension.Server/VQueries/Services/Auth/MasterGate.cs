using CombineQueries.Api.Services.Speech;

namespace CombineQueries.Api.Services.Auth;

public class MasterGate
{
    private readonly RequestDelegate _next;
    private readonly ILogger<MasterGate> _logger;
    private readonly bool _enabled;

    public MasterGate(RequestDelegate next, ILogger<MasterGate> logger, IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _enabled = !string.IsNullOrEmpty(configuration["Auth:Codeword"]);
    }

    public async Task Invoke(HttpContext context, ISpeech speech)
    {
        string path = context.Request.Path.Value ?? "";

        // Single tenant: the codeword authorizes the whole server for the session, no per-IP binding.
        // The client's address hops between IPv4/IPv6 and Fly's proxy, so keying by it would 403 at random.
        if (_enabled && Gated(path) && !speech.Authorized)
        {
            _logger.LogWarning("gate: {Path} rejected - server not authorized (codeword not accepted yet)", path);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;

            return;
        }

        await _next(context);
    }

    private static bool Gated(string path) =>
        path.StartsWith("/init") || path.StartsWith("/c/") || path.StartsWith("/t/") || path.StartsWith("/d/") || path.StartsWith("/h/") || path.StartsWith("/f/") || path.StartsWith("/g/");
}
