using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

using MediatR;

using CombineQueries.Api.Controllers.Translator.Handlers.Init;
using CombineQueries.Api.Controllers.Translator.Handlers.Tail;
using CombineQueries.Api.Controllers.Translator.Handlers.Combine;
using CombineQueries.Api.Controllers.Translator.Handlers.Hyper;

namespace CombineQueries.Api.Controllers.Translator;

[Route("translator")]
public class TranslatorController : Controller
{
    private readonly IMediator _mediator;

    public TranslatorController(IMediator mediator) => _mediator = mediator;

    // Единственная ручка на модель-биндинге, и это осознанно: алфавит приезжает PERCENT-ENCODED,
    // биндер его декодирует сам. Сырым слать нельзя ни при каких условиях - в алфавите есть '#',
    // он начинает фрагмент, и всё после него до сервера просто не доедет.
    [AllowAnonymous] [HttpGet("/init")] public Task<InitResponse> Init(InitRequest request) => _mediator.Send(request);

    // Руны едут в QUERY, а не в пути: в пути слэш разрывает сегмент и роут не матчится,
    // а в query / и ? легальны. Читаем СЫРУЮ строку и не разбираем её на пары ключ-значение,
    // иначе & и = из wire-алфавита развалят разбор.
    [AllowAnonymous] [HttpGet("/n")] public Task<TailResponse> Tail() => _mediator.Send(new TailRequest { Value = RawInt() });

    [AllowAnonymous] [HttpGet("/c")] public Task<CombineResponse> Combine() => _mediator.Send(new CombineRequest { Runes = RawArg() });

    // hyper: известная ссылка одним запросом вместо всей цепочки /n + куски
    [AllowAnonymous] [HttpGet("/h")] public Task<HyperResponse> Hyper() => _mediator.Send(new HyperRequest { Value = RawInt() });

    // всё после первого '=' - это и есть аргумент, каким бы он ни был
    private string RawArg()
    {
        string raw = Request.QueryString.Value ?? "";
        int eq = raw.IndexOf('=');

        return eq < 0 ? "" : raw.Substring(eq + 1);
    }

    private int RawInt() => int.TryParse(RawArg(), out int v) ? v : -1;
}
