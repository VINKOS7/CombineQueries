using MediatR;

using CombineQueries.Api.Services.AFST;
using CombineQueries.Api.Services.Forwarder;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Hyper;

public class HyperHandler : IRequestHandler<HyperRequest, HyperResponse>
{
    private readonly ILogger<HyperHandler> _logger;
    private readonly ISpeech _afst;
    private readonly IForward _forwarder;

    public HyperHandler(ILogger<HyperHandler> logger, IForward forwarder, ISpeech afst)
    {
        _logger = logger;
        _forwarder = forwarder;
        _afst = afst;
    }

    public async Task<HyperResponse> Handle(HyperRequest request, CancellationToken cancellationToken)
    {
        if (_afst.Alphabet is null) throw new Exception("CRIT: /init was not called");

        string? url = _afst.Resolve(request.Value);

        if (url is null)
        {
            _logger.LogWarning($"hyper: handle {request.Value} is unknown - client must resend the full url");

            return new HyperResponse { Known = false };
        }

        var forwarded = await _forwarder.GetAsync(url, cancellationToken);

        long first = _afst.FirstSendMsOf(request.Value);

        _logger.LogInformation(
            $"hyper: handle {request.Value} -> '{url}' | 1 request, {forwarded.ElapsedMs} ms "
            + $"vs first send {first} ms"
            + (first > 0 ? $" -> x{(double)first / Math.Max(forwarded.ElapsedMs, 1):F1}" : ""));

        return new HyperResponse
        {
            Known = true,
            ForwardedUrl = url,
            Response = forwarded.Body,
            ElapsedMs = forwarded.ElapsedMs,
            FirstSendMs = first
        };
    }
}
