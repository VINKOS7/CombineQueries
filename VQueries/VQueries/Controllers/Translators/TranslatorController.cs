using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

using MediatR;

using CombineQueries.Domain.Aggregates.Translator.types;
using CombineQueries.Api.Controllers.Translators.Handlers.Init;
using CombineQueries.Api.Controllers.Translators.Handlers.Combine;
using CombineQueries.Api.Controllers.Translators.Handlers.Tail;
using CombineQueries.Api.Controllers.Translators.Handlers.Hyper;


namespace CombineQueries.Api.Controllers.Translators;

[Route("translator")]
public class TranslatorController : Controller
{
    private readonly IMediator _mediator;

    public TranslatorController(IMediator mediator) => _mediator = mediator;

    [AllowAnonymous] [HttpGet("/init")] public Task<InitResponse> Init(InitRequest request) => _mediator.Send(request);

    [AllowAnonymous] [HttpGet("/c/{runes}")] public Task<MergeResponse> Combine(string runes) => _mediator.Send(new MergeRequest { Runes = runes });

    [AllowAnonymous][HttpGet("/m/{runes}")] public Task<MergeResponse> Merge(string runes) => _mediator.Send(new MergeRequest { Runes = runes });

    [AllowAnonymous] [HttpGet("/t/{runes}")] public Task<TailResponse> Tail(string runes) => _mediator.Send(new TailRequest { Runes = runes, Type = TypeQuery.Fragmentate });

    [AllowAnonymous] [HttpGet("/d/{runes}")] public Task<TailResponse> Direct(string runes) => _mediator.Send(new TailRequest { Runes = runes, Type = TypeQuery.Direct });

    [AllowAnonymous] [HttpGet("/h/{hyper:int}")] public Task<HyperResponse> Hyper(int hyper) => _mediator.Send(new HyperRequest { Value = hyper });

}
