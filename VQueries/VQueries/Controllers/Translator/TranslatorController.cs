using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

using MediatR;

using CombineQueries.Api.Controllers.Translator.Handlers.Init;
using CombineQueries.Api.Controllers.Translator.Handlers.Count;
using CombineQueries.Api.Controllers.Translator.Handlers.Combine;
using CombineQueries.Api.Controllers.Translator.Handlers.Handle;

namespace CombineQueries.Api.Controllers.Translator;

[Route("translator")]
public class TranslatorController : Controller
{
    private readonly IMediator _mediator;

    public TranslatorController(IMediator mediator) => _mediator = mediator;

    [AllowAnonymous] [HttpGet("/init")] public Task<InitResponse> Init(InitRequest request) => _mediator.Send(request);

    // Руны едут в QUERY, а не в пути: в пути слэш разрывает сегмент и роут не матчится,
    // а в query / и ? легальны. Читаем СЫРУЮ строку и не разбираем её на пары ключ-значение,
    // иначе & и = из wire-алфавита развалят разбор.
    [AllowAnonymous] [HttpGet("/n")] public Task<CountResponse> Count() => _mediator.Send(new CountRequest { Value = RawInt() });

    [AllowAnonymous] [HttpGet("/m")] public Task<CombineResponse> Combine() => _mediator.Send(new CombineRequest { Runes = RawArg() });

    [AllowAnonymous] [HttpGet("/h")] public Task<HandleResponse> Handle() => _mediator.Send(new HandleRequest { Value = RawInt() });

    // всё после первого '=' - это и есть аргумент, каким бы он ни был
    private string RawArg()
    {
        string raw = Request.QueryString.Value ?? "";
        int eq = raw.IndexOf('=');

        return eq < 0 ? "" : raw.Substring(eq + 1);
    }

    private int RawInt() => int.TryParse(RawArg(), out int v) ? v : -1;
}
