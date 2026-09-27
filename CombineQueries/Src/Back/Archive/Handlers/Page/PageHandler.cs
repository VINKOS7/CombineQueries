using MediatR;

using CombineQueries.Api.Services.Speech;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Page;

// Развязка-2: /g/<page> ставит страницу для следующего VF (адрес L3: id = page*DfaSize + offset).
// Тело ответа клиент не читает - fire-and-forget, как /c/ и /f/.
public class PageHandler(ILogger<PageHandler> logger, ISpeech speech) : IRequestHandler<PageRequest, PageResponse>
{
    public Task<PageResponse> Handle(PageRequest request, CancellationToken cancellationToken)
    {
        if (speech.Alphabet is null) throw new Exception("PageHandler: /init was not called");

        speech.SetFragmentPage(request.Page);

        logger.LogInformation("page: L3 page {Page} set for next VF", request.Page);

        return Task.FromResult(new PageResponse());
    }
}
