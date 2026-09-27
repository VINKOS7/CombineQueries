using MediatR;

using CombineQueries.Domain.Aggregates.Account;

namespace CombineQueries.Api.Controllers.Accounts.Handlers.Active;

public class ActiveHandler(IAccountRepo accountRepo, IConfiguration configuration, ILogger<ActiveHandler> logger) : IRequestHandler<ActiveRequest, ActiveResponse>
{
    public async Task<ActiveResponse> Handle(ActiveRequest request, CancellationToken cancellationToken)
    {
        MasterOnly.Ensure(request, configuration);

        var account = await accountRepo.GetAsync(request.Id) ?? throw new Exception("domain error: account not found");

        account.Active = request.Active;

        await accountRepo.UnitOfWork.SaveEntitiesAsync(cancellationToken);

        logger.LogInformation("accounts: {Id} -> {Verdict}", account.Id, account.Active ? "active" : "disabled");

        return new ActiveResponse { Id = account.Id, Active = account.Active };
    }
}
