using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

using MediatR;

using CombineQueries.Api.Controllers.TranslatorController.Handlers.Init;
using CombineQueries.Api.Controllers.TranslatorController.Handlers.CQMergeSend;

namespace CombineQueries.Api.Controllers.TranslatorController;

[Route("translator")]
public class TranslatorController : Controller
{
    private readonly IMediator _mediator;

    public TranslatorController(IMediator mediator) => _mediator = mediator;

    [AllowAnonymous] [HttpGet("alphabet/init")] public Task<InitResponse> Init(InitRequest request) => _mediator.Send(request);

    [AllowAnonymous] [HttpGet("runes/single")] public Task<MergeResponse> Send(MergeRequest request) => _mediator.Send(request);

    [AllowAnonymous] [HttpGet("runes/combine")] public Task<MergeResponse> Merged(MergeRequest request) => _mediator.Send(request);
}
