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

    public static string KeyOf(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "";

    public async Task Invoke(HttpContext context, ISpeech speech)
    {
        string path = context.Request.Path.Value ?? "";

        if (_enabled && Gated(path) && !speech.IsMaster(KeyOf(context)))
        {
            _logger.LogWarning("gate: {Path} from {Ip} rejected - not the master", path, KeyOf(context));

            context.Response.StatusCode = StatusCodes.Status403Forbidden;

            return;
        }

        await _next(context);
    }

    private static bool Gated(string path) =>
        path.StartsWith("/init") || path.StartsWith("/c/") || path.StartsWith("/t/") || path.StartsWith("/d/") || path.StartsWith("/h/");
}
