using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

using MediatR;

using CombineQueries.Domain.Aggregates.Translator.types;
using CombineQueries.Api.Controllers.Translators.Handlers.Combine;
using CombineQueries.Api.Controllers.Translators.Handlers.Tail;
using CombineQueries.Api.Controllers.Translators.Handlers.Head;
using CombineQueries.Api.Controllers.Translators.Handlers.Hyper;
using CombineQueries.Api.Controllers.Translators.Handlers.Credit;
using CombineQueries.Api.Controllers.Translators.Handlers.Code;


namespace CombineQueries.Api.Controllers.Translators;

[Route("translator")]
public class TranslatorController : Controller
{
    private readonly IMediator _mediator;

    public TranslatorController(IMediator mediator) => _mediator = mediator;

    [AllowAnonymous] [HttpGet("/h/{jump:int}/{count:int}/{sign:int}")] public Task<HyperResponse> HyperRange(int jump, int count, int sign) 
        => _mediator.Send(new HyperRequest { Value = jump, Count = count, Sign = sign });
    
    [AllowAnonymous] [HttpGet("/tc/{sign:int}")] public Task<CreditResponse> TakeCredit(int sign) 
        => _mediator.Send(new CreditRequest { Sign = sign });

    [AllowAnonymous] [HttpGet("/hd/{fragment:int}/{shortened:int}/{sign:int}")] public Task<HeadResponse> HeadFrom(int fragment, int shortened, int sign) 
        => _mediator.Send(new HeadRequest { Fragment = fragment, Base = shortened, Sign = sign });

    [AllowAnonymous] [HttpGet("/c/{runes}/{id:int}/{page:int}/{hop:int}/{q:int}")] public Task<CombineResponse> Combine(string runes, int id, int page, int hop, int q)
        => _mediator.Send(new CombineRequest { Runes = runes, Id = id, Page = page, Hop = hop, Q = q });
 
    [AllowAnonymous] [HttpGet("/d/{runes}")] public Task<TailResponse> Direct(string runes) 
        => _mediator.Send(new TailRequest { Runes = runes, Type = TypeQuery.Direct });

    [AllowAnonymous] [HttpGet("/t/{runes}/{sign:int}")] public Task<TailResponse> Tail(string runes, int sign)
        => _mediator.Send(new TailRequest { Runes = runes, Sign = sign, Type = TypeQuery.Fragmentate });

    // Кусок и закрытие одним запросом: адрес, целиком накрытый одним куском словаря, стоит один
    // запрос вместо двух. Хвостовых символов у такого адреса нет по определению.
    [AllowAnonymous] [HttpGet("/cf/{id:int}/{sign:int}")] public Task<TailResponse> CloseWith(int id, int sign)
        => _mediator.Send(new TailRequest { Runes = "", Sign = sign, Fragment = id, Type = TypeQuery.Fragmentate });

    [AllowAnonymous] [HttpGet("/k/{seg}")] public Task<CodeAppendResponse> Code(string seg) => _mediator.Send(new CodeAppendRequest { Segment = seg });

    [AllowAnonymous] [HttpGet("/kf")]
    public async Task<IActionResult> CodeVerify()
    {
        var result = await _mediator.Send(new CodeVerifyRequest());

        return result.Bound ? Ok(result) : StatusCode(StatusCodes.Status403Forbidden, result);
    }
}
