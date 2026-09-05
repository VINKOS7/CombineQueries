using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

using MediatR;

using CombineQueries.Domain.Aggregates.Translator.types;
using CombineQueries.Api.Controllers.Translators.Handlers.Init;
using CombineQueries.Api.Controllers.Translators.Handlers.Combine;
using CombineQueries.Api.Controllers.Translators.Handlers.CombineNew;
using CombineQueries.Api.Controllers.Translators.Handlers.Tail;
using CombineQueries.Api.Controllers.Translators.Handlers.Hyper;
using CombineQueries.Api.Controllers.Translators.Handlers.Fragment;
using CombineQueries.Api.Controllers.Translators.Handlers.Code;


namespace CombineQueries.Api.Controllers.Translators;

[Route("translator")]
public class TranslatorController : Controller
{
    private readonly IMediator _mediator;

    public TranslatorController(IMediator mediator) => _mediator = mediator;

    [AllowAnonymous] [HttpGet("/k/{seg}")] public Task<CodeAppendResponse> Code(string seg) => _mediator.Send(new CodeAppendRequest { Segment = seg });

    [AllowAnonymous] [HttpGet("/kf")]
    public async Task<IActionResult> CodeVerify()
    {
        var result = await _mediator.Send(new CodeVerifyRequest());

        return result.Bound ? Ok(result) : StatusCode(StatusCodes.Status403Forbidden, result);
    }

    [AllowAnonymous] [HttpGet("/init")] public Task<InitResponse> Init(InitRequest request) => _mediator.Send(request);

    [AllowAnonymous] [HttpGet("/c/{runes}")] public Task<Handlers.Combine.CombineResponse> Combine(string runes) => _mediator.Send(new Handlers.Combine.CombineRequest { Runes = runes });

    [AllowAnonymous] [HttpGet("/t/{runes}")] public Task<TailResponse> Tail(string runes) => _mediator.Send(new TailRequest { Runes = runes, Type = TypeQuery.Fragmentate });

    [AllowAnonymous] [HttpGet("/d/{runes}")] public Task<TailResponse> Direct(string runes) => _mediator.Send(new TailRequest { Runes = runes, Type = TypeQuery.Direct });

    [AllowAnonymous] [HttpGet("/h/{hyper:int}")] public Task<HyperResponse> Hyper(int hyper) => _mediator.Send(new HyperRequest { Value = hyper });

    // Динамический фрагмент: /f/<id> вклинивает известную подстроку в поток сборки (см. FragmentHandler).
    [AllowAnonymous] [HttpGet("/f/{id:int}")] public Task<FragmentResponse> DyFragment(int id) => _mediator.Send(new FragmentRequest { Id = id });

    [AllowAnonymous][HttpGet("/c/{runes}/{id:int}/{page:int}/{q:int}")] public Task<Handlers.CombineNew.CombineResponse> CombineNew(string runes, int id, int page, int q) => _mediator.Send(new Handlers.CombineNew.CombineRequest { Runes = runes, Id = id, Page = page, Q = q });

    // Развязка-2 (L3): /g/<page> ставит страницу для следующего VF.
    [AllowAnonymous][HttpGet("/g/{page:int}")] public Task<Handlers.Page.PageResponse> Page(int page) => _mediator.Send(new Handlers.Page.PageRequest { Page = page });


}
