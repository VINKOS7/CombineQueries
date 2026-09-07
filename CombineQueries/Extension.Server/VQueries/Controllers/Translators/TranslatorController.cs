using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

using MediatR;

using CombineQueries.Domain.Aggregates.Translator.types;
using CombineQueries.Api.Controllers.Translators.Handlers.Combine;
using CombineQueries.Api.Controllers.Translators.Handlers.Tail;
using CombineQueries.Api.Controllers.Translators.Handlers.Hyper;
using CombineQueries.Api.Controllers.Translators.Handlers.Code;


namespace CombineQueries.Api.Controllers.Translators;

[Route("translator")]
public class TranslatorController : Controller
{
    private readonly IMediator _mediator;

    public TranslatorController(IMediator mediator) => _mediator = mediator;

    // Хвост несёт ПОДПИСЬ - очередную из выданной на connect последовательности. Живёт здесь, а не
    // в /c/, потому что у хвоста свой маленький пул: подпись множит 8931, а не 830 584.
    [AllowAnonymous] [HttpGet("/t/{runes}/{sign:int}")] public Task<TailResponse> Tail(string runes, int sign) => _mediator.Send(new TailRequest { Runes = runes, Sign = sign, Type = TypeQuery.Fragmentate });

    [AllowAnonymous] [HttpGet("/d/{runes}")] public Task<TailResponse> Direct(string runes) => _mediator.Send(new TailRequest { Runes = runes, Type = TypeQuery.Direct });

    [AllowAnonymous] [HttpGet("/h/{hyper:int}")] public Task<HyperResponse> Hyper(int hyper) => _mediator.Send(new HyperRequest { Value = hyper });

    [AllowAnonymous] [HttpGet("/c/{runes}/{id:int}/{page:int}/{hop:int}/{q:int}")] public Task<CombineResponse> Combine(string runes, int id, int page, int hop, int q) => _mediator.Send(new CombineRequest { Runes = runes, Id = id, Page = page, Hop = hop, Q = q });

    [AllowAnonymous] [HttpGet("/k/{seg}")] public Task<CodeAppendResponse> Code(string seg) => _mediator.Send(new CodeAppendRequest { Segment = seg });

    [AllowAnonymous] [HttpGet("/kf")]
    public async Task<IActionResult> CodeVerify()
    {
        var result = await _mediator.Send(new CodeVerifyRequest());

        return result.Bound ? Ok(result) : StatusCode(StatusCodes.Status403Forbidden, result);
    }
}
