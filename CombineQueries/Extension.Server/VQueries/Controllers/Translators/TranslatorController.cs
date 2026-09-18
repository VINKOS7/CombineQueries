using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

using MediatR;

using CombineQueries.Domain.Aggregates.Translator.types;
using CombineQueries.Api.Controllers.Translators.Handlers.Head;
using CombineQueries.Api.Controllers.Translators.Handlers.Hyper;
using CombineQueries.Api.Controllers.Translators.Handlers.Combine;
using CombineQueries.Api.Controllers.Translators.Handlers.Credit;
using CombineQueries.Api.Controllers.Translators.Handlers.Tail;
using CombineQueries.Api.Services.Speech;

namespace CombineQueries.Api.Controllers.Translators;

[Route("translator")]
public class TranslatorController : Controller
{
    private readonly IMediator _mediator;
    private readonly ISpeech _speech;

    public TranslatorController(IMediator mediator, ISpeech speech)
    {
        _mediator = mediator;
        _speech = speech;
    }

    private async Task<TAnswer> Signed<TAnswer>(Task<TAnswer> asked) where TAnswer : ISigned
    {
        var answer = await asked;

        answer.Signs = _speech.TakeFreshSigns();

        return answer;
    }

    [AllowAnonymous] [HttpGet("/h/{jump:int}/{count:int}/{sign:int}")] public Task<HyperResponse> Hyper(int jump, int count, int sign) 
        => Signed(_mediator.Send(new HyperRequest { Value = jump, Count = count, Sign = sign }));
    
    [AllowAnonymous] [HttpGet("/tc/{sign:int}")] public Task<CreditResponse> TakeCredit(int sign) 
        => Signed(_mediator.Send(new CreditRequest { Sign = sign }));

    [AllowAnonymous] [HttpGet("/hd/{fragment:int}/{shortened:int}/{sign:int}")] public Task<HeadResponse> Head(int fragment, int shortened, int sign) 
        => Signed(_mediator.Send(new HeadRequest { Fragment = fragment, Base = shortened, Sign = sign }));

    [AllowAnonymous] [HttpGet("/c/{runes}/{id:int}/{page:int}/{hop:int}/{q:int}")] public Task<CombineResponse> Combine(string runes, int id, int page, int hop, int q)
        => _mediator.Send(new CombineRequest { Runes = runes, Id = id, Page = page, Hop = hop, Q = q });
 
    [AllowAnonymous] [HttpGet("/d/{runes}")] public Task<TailResponse> Direct(string runes) 
        => _mediator.Send(new TailRequest { Runes = runes, Type = TypeQuery.Direct });

    [AllowAnonymous] [HttpGet("/t/{runes}/{sign:int}")] public Task<TailResponse> Tail(string runes, int sign)
        => Signed(_mediator.Send(new TailRequest { Runes = runes, Sign = sign, Type = TypeQuery.Fragmentate }));

    [AllowAnonymous] [HttpGet("/cf/{id:int}/{sign:int}")] public Task<TailResponse> CloseWith(int id, int sign)
        => Signed(_mediator.Send(new TailRequest { Runes = "", Sign = sign, Fragment = id, Type = TypeQuery.Fragmentate }));
}
