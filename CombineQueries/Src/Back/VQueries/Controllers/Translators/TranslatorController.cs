using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

using MediatR;

using CombineQueries.Domain.Aggregates.Translator.types;
using CombineQueries.Api.Controllers.Translators.Handlers.Head;
using CombineQueries.Api.Controllers.Translators.Handlers.Hyper;
using CombineQueries.Api.Controllers.Translators.Handlers.Combine;
using CombineQueries.Api.Controllers.Translators.Handlers.Credit;
using CombineQueries.Api.Controllers.Translators.Handlers.Tail;

namespace CombineQueries.Api.Controllers.Translators;

[Route("translator")]
public class TranslatorController : Controller
{
    private readonly IMediator _mediator;

    public TranslatorController(IMediator mediator) => _mediator = mediator;

    [AllowAnonymous] [HttpGet("/h/{jump:int}/{count:int}/{sign:int}")] public Task<HyperResponse> Hyper(int jump, int count, int sign)
        => _mediator.Send(new HyperRequest { Value = jump, Count = count, Sign = sign });

    [AllowAnonymous] [HttpGet("/tc/{sign:int}")] public Task<CreditResponse> TakeCredit(int sign)
        => _mediator.Send(new CreditRequest { Sign = sign });

    [AllowAnonymous] [HttpGet("/hd/{fragment:int}/{shortened:int}/{complete:int}/{sign:int}")] public Task<HeadResponse> Head(int fragment, int shortened, int complete, int sign)
        => _mediator.Send(new HeadRequest { Fragment = fragment, Base = shortened, Complete = complete != 0, Sign = sign });

    [AllowAnonymous] [HttpGet("/c/{runes}/{id:int}/{page:int}/{hop:int}/{q:int}/{sign:int?}")] public Task<object> Combine(string runes, int id, int page, int hop, int q, int? sign)
        => _mediator.Send(new CombineRequest { Runes = runes, Id = id, Page = page, Hop = hop, Q = q, Sign = sign ?? -1 });

    [AllowAnonymous] [HttpGet("/t/{runes}/{merge:int}/{sign:int}")] public Task<TailResponse> Tail(string runes, int merge, int sign)
        => _mediator.Send(new TailRequest { Runes = runes, Merge = merge != 0, Sign = sign, Type = TypeQuery.Fragmentate });

    [AllowAnonymous] [HttpGet("/sf/{id:int}/{merge:int}/{sign:int}")] public Task<TailResponse> SingleFragment(int id, int merge, int sign)
        => _mediator.Send(new TailRequest { Runes = "", Merge = merge != 0, Sign = sign, Fragment = id, Type = TypeQuery.Fragmentate });

    //[AllowAnonymous] [HttpGet("/d/{runes}")] public Task<TailResponse> Direct(string runes)
    //    => _mediator.Send(new TailRequest { Runes = runes, Type = TypeQuery.Direct });
}
