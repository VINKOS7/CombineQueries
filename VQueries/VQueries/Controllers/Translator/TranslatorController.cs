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

    // init - разовый статический URL, не в комбинаторном пуле, ему можно оставить нормальный путь
    [AllowAnonymous] [HttpGet("alphabet/init")] public Task<InitResponse> Init(InitRequest request) => _mediator.Send(request);

    // весь пул wire-рун печётся как baseUrl+буквальные_символы, без пути/query - единственный
    // плоский роут на корне домена, ловит один path-сегмент целиком. merge vs send различает
    // MergeHandler по трейлинг-маркеру внутри самого runes, не путь.
    [AllowAnonymous] [HttpGet("/{runes}")] public Task<CombineResponse> Combine(string runes) => _mediator.Send(new CombineRequest { Runes = runes });
}
