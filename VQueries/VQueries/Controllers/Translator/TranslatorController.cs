using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

using MediatR;

using CombineQueries.Api.Controllers.Translators.Handlers.Init;
using CombineQueries.Api.Controllers.Translators.Handlers.Tail;
using CombineQueries.Api.Controllers.Translators.Handlers.Combine;
using CombineQueries.Api.Controllers.Translators.Handlers.Hyper;

namespace CombineQueries.Api.Controllers.Translators;

[Route("translator")]
public class TranslatorController : Controller
{
    private readonly IMediator _mediator;

    public TranslatorController(IMediator mediator) => _mediator = mediator;

    [AllowAnonymous] [HttpGet("/init")] public Task<InitResponse> Init(InitRequest request) => _mediator.Send(request);

    [AllowAnonymous] [HttpGet("/n")] public Task<TailResponse> Tail() => _mediator.Send(TailRequest.From(Request.QueryString.Value));

    [AllowAnonymous] [HttpGet("/c")] public Task<CombineResponse> Combine() => _mediator.Send(CombineRequest.From(Request.QueryString.Value));

    [AllowAnonymous] [HttpGet("/h")] public Task<HyperResponse> Hyper() => _mediator.Send(HyperRequest.From(Request.QueryString.Value));

}
