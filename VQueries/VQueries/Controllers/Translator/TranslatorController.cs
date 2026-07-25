using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.WebUtilities;

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

    [AllowAnonymous] [HttpGet("/init")] public Task<InitResponse> Init() => _mediator.Send(RawInit());

    // Руны едут в QUERY, а не в пути: в пути слэш разрывает сегмент и роут не матчится,
    // а в query / и ? легальны. Читаем СЫРУЮ строку и не разбираем её на пары ключ-значение,
    // иначе & и = из wire-алфавита развалят разбор.
    [AllowAnonymous] [HttpGet("/n")] public Task<CountResponse> Count() => _mediator.Send(new CountRequest { Value = RawInt() });

    [AllowAnonymous] [HttpGet("/m")] public Task<CombineResponse> Combine() => _mediator.Send(new CombineRequest { Runes = RawArg() });

    [AllowAnonymous] [HttpGet("/h")] public Task<HandleResponse> Handle() => _mediator.Send(new HandleRequest { Value = RawInt() });

    // /init страдал ровно тем же, чем страдали руны в пути, только незаметно: модель-биндинг
    // разбирает query на пары, а в АЛФАВИТЕ живут & и = - он обрезался на первом же &.
    // Симптом тихий: 200 OK, но сервер брал 42 символа вместо 59, и дальше весь декод врал.
    //
    // Поэтому alphabet ОБЯЗАН идти ПОСЛЕДНИМ параметром: всё после "alphabet=" - сырой хвост,
    // а служебные параметры стоят до него и разбираются обычным способом.
    private InitRequest RawInit()
    {
        const string tail = "alphabet=";

        string raw = Request.QueryString.Value ?? "";
        int at = raw.IndexOf(tail, StringComparison.Ordinal);

        string alphabet = at < 0 ? "" : raw.Substring(at + tail.Length);
        var head = QueryHelpers.ParseQuery(at < 0 ? raw : raw.Substring(0, at));

        return new InitRequest
        {
            Alphabet = alphabet,
            baseForwardUrl = head.TryGetValue("baseQuery", out var b) ? b.ToString() : "",
            RuneSize = head.TryGetValue("runeSize", out var r) && int.TryParse(r, out int v) ? v : 2
        };
    }

    // всё после первого '=' - это и есть аргумент, каким бы он ни был
    private string RawArg()
    {
        string raw = Request.QueryString.Value ?? "";
        int eq = raw.IndexOf('=');

        return eq < 0 ? "" : raw.Substring(eq + 1);
    }

    private int RawInt() => int.TryParse(RawArg(), out int v) ? v : -1;
}
