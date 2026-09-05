using MediatR;

using CombineQueries.Api.Services.Speech;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Fragment;

// Врезка динамического фрагмента в поток сборки: один /f/<id> = одна известная подстрока
// вместо пачки /c/. Как и /c/, тело ответа клиент не читает - fire-and-forget.
public class FragmentHandler(ILogger<FragmentHandler> logger, ISpeech speech) : IRequestHandler<FragmentRequest, FragmentResponse>
{
    public Task<FragmentResponse> Handle(FragmentRequest request, CancellationToken cancellationToken)
    {
        if (speech.Alphabet is null || speech.RuneAlphabet is null) throw new Exception("FragmentHandler: /init was not called");

        if (speech.ResolveVirtualFragment(request.Id) is null) logger.LogWarning("fragment: id {Id} is unknown - assembly will drop it", request.Id);

        int received = speech.AcceptVirtualFragment(request.Id);

        logger.LogInformation("fragment: id {Id} accepted, piece {Received}", request.Id, received);

        return Task.FromResult(new FragmentResponse { Received = received });
    }
}
