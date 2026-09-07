using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

using MediatR;

using CombineQueries.Api.Controllers.Accounts.Handlers.Connect;

namespace CombineQueries.Api.Controllers.Accounts;

[Route("accounts")]
public class AccountController : Controller
{
    private readonly IMediator _mediator;

    public AccountController(IMediator mediator) => _mediator = mediator;

    [AllowAnonymous] [HttpGet("/connect")] public Task<ConnectResponse> Connect(ConnectRequest request) => _mediator.Send(request);
}
