using MediatR;

using CombineQueries.Api.Services.AFST;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Combine;

public class CombineHandler : IRequestHandler<CombineRequest, CombineResponse>
{
    private readonly ILogger<CombineHandler> _logger;
    private readonly IAFST _afst;

    public CombineHandler(ILogger<CombineHandler> logger, IAFST afst)
    {
        _logger = logger;
        _afst = afst;
    }

    public Task<CombineResponse> Handle(CombineRequest request, CancellationToken cancellationToken)
    {
        if (_afst.Alphabet is null) throw new Exception("CombineHandler: /init was not called");

        if (string.IsNullOrEmpty(request.Runes)) throw new Exception("CombineHandler: empty runes");

        int received = _afst.Accept(request.Runes);

        _logger.LogInformation($"combine: rune {received} accepted");

        return Task.FromResult(new CombineResponse { Received = received });
    }
}
