using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

using MediatR;

using CombineQueries.Api.Controllers.Accounts.Handlers.Connect;

namespace CombineQueries.Api.Controllers.Accounts;

// Аккаунты. Контроллер тонкий, как TranslatorController: только Send.
[Route("accounts")]
public class AccountController : Controller
{
    private readonly IMediator _mediator;

    public AccountController(IMediator mediator) => _mediator = mediator;

    // Подключение мира: аккаунт заявляет контекст и получает тёплый словарь. Живёт здесь, а не в
    // TranslatorController, потому что это операция АККАУНТА - Init/Remember его внутренние методы.
    [AllowAnonymous] [HttpGet("/connect")] public Task<ConnectResponse> Connect(ConnectRequest request) => _mediator.Send(request);
}
