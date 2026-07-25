using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

using MediatR;

using CombineQueries.Api.Controllers.Translator.Handlers.Init;
using CombineQueries.Api.Controllers.Translator.Handlers.Combine;
using CombineQueries.Api.Controllers.Translator.Handlers.Merge;
using CombineQueries.Api.Controllers.Translator.Handlers.MergeSend;

namespace CombineQueries.Api.Controllers.Translator;

[Route("translator")]
public class TranslatorController : Controller
{
    private readonly IMediator _mediator;

    public TranslatorController(IMediator mediator) => _mediator = mediator;

    [AllowAnonymous] [HttpGet("/init")] public Task<InitResponse> Init(InitRequest request) => _mediator.Send(request);

    [AllowAnonymous] [HttpGet("/{runes}")] public Task<CombineResponse> Combine(string runes) => _mediator.Send(new CombineRequest { Runes = runes });

    [AllowAnonymous] [HttpGet("/m/{runes}")] public Task<MergeResponse> Merge(string runes) => _mediator.Send(new MergeRequest { Runes = runes });

    [AllowAnonymous] [HttpGet("/s/{runes}")] public Task<MergeSendResponse> MergeSend(string runes) => _mediator.Send(new MergeSendRequest { Runes = runes, Mode = CombineMode.Simple });

    [AllowAnonymous] [HttpGet("/t/{runes}")] public Task<MergeSendResponse> MergeSendTree(string runes) => _mediator.Send(new MergeSendRequest { Runes = runes, Mode = CombineMode.Tree });
}
