using MediatR;

using CombineQueries.Api.Services.AFST;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Combine;

public class CombineHandler : IRequestHandler<CombineRequest, CombineResponse>
{
    private readonly ILogger<CombineHandler> _logger;
    private readonly ISpeech _afst;

    public CombineHandler(ILogger<CombineHandler> logger, ISpeech afst)
    {
        _logger = logger;
        _afst = afst;
    }

    public Task<CombineResponse> Handle(CombineRequest request, CancellationToken cancellationToken)
    {

        switch(request.Type)
        {
            case TypeCombine.Direct:

                // Handle direct combine logic
                break;

            case TypeCombine.Fragmentate:
                if (_afst.Alphabet is null) throw new Exception("CombineHandler: /init was not called");

                if (string.IsNullOrEmpty(request.Runes)) throw new Exception("CombineHandler: empty runes");

                received = _afst.Accept(request.Runes);

                _logger.LogInformation($"combine: rune {received} accepted");

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request.Type), request.Type, "Unexpected TypeCombine value");
        }

        return Task.FromResult(new CombineResponse { Received = received });
    }
}
