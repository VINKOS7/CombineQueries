using MediatR;

using CombineQueries.Api.Services.Speech;
using CombineQueries.Api.Services.Forwarder;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Hyper;

public class HyperHandler(ILogger<HyperHandler> logger, IForward forwarder, ISpeech speech) : IRequestHandler<HyperRequest, HyperResponse>
{
    public async Task<HyperResponse> Handle(HyperRequest request, CancellationToken cancellationToken)
    {
        if (speech.Alphabet is null) throw new Exception("CRIT: /init was not called");

        string? url = speech.Resolve(request.Value);

        if (url is null)
        {
            logger.LogWarning("hyper: handle {Handle} is unknown - client must resend the full url", request.Value);

            return new HyperResponse { Known = false };
        }

        var forwarded = await forwarder.GetAsync(url, cancellationToken);

        long first = speech.FirstSendMsOf(request.Value);

        logger.LogInformation("hyper: handle {Handle} -> {Url} | 1 request, {ElapsedMs} ms vs first send {FirstMs} ms",
            request.Value, url, forwarded.ElapsedMs, first);

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
