using CombineQueries.Api.Controllers.Accounts.Handlers.Code;
using CombineQueries.Api.Controllers.Accounts.Handlers.Connect;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CombineQueries.Api.Controllers.Accounts;

[Route("accounts")]
public class AccountController : Controller
{
    private readonly IMediator _mediator;

    public AccountController(IMediator mediator) => _mediator = mediator;

    [AllowAnonymous] [HttpGet("/connect")] public Task<ConnectResponse> Connect(ConnectRequest request) => _mediator.Send(request);

    [AllowAnonymous][HttpGet("/k/{seg}")] public Task<CodeAppendResponse> Code(string seg) => _mediator.Send(new CodeAppendRequest { Segment = seg });
    
    [AllowAnonymous] [HttpGet("/kf")]
    public async Task<IActionResult> CodeVerify()
    {
        var result = await _mediator.Send(new CodeVerifyRequest());

        return result.Bound ? Ok(result) : StatusCode(StatusCodes.Status403Forbidden, result);
    }
}
