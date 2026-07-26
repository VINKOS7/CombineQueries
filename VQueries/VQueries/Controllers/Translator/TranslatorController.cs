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

    [AllowAnonymous] [HttpGet("/init")] public Task<InitResponse> Init(InitRequest request) => _mediator.Send(request);

    [AllowAnonymous] [HttpGet("/n")] public Task<TailResponse> Tail() => _mediator.Send(new TailRequest { Value = RawInt() });

    [AllowAnonymous] [HttpGet("/c")] public Task<CombineResponse> Combine() => _mediator.Send(new CombineRequest { Runes = RawArg() });

    [AllowAnonymous] [HttpGet("/h")] public Task<HyperResponse> Hyper() => _mediator.Send(new HyperRequest { Value = RawInt() });


    //after sometime will resolve this garbage
    private string RawArg() => 
        (Request.QueryString.Value ?? string.Empty).IndexOf('=') < 0 ? "" : Request.QueryString.Value ?? string.Empty.Substring((Request.QueryString.Value ?? string.Empty).IndexOf('=') + 1);

    private int RawInt() => int.TryParse(RawArg(), out int v) ? v : -1;
}
