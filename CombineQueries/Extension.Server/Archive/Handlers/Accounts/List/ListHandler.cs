using MediatR;

using CombineQueries.Domain.Aggregates.Account;

namespace CombineQueries.Api.Controllers.Accounts.Handlers.List;

public class ListHandler(IAccountRepo accountRepo, IConfiguration configuration) : IRequestHandler<ListRequest, ListResponse>
{
    private const int Tail = 4;

    public async Task<ListResponse> Handle(ListRequest request, CancellationToken cancellationToken)
    {
        MasterOnly.Ensure(request, configuration);

        var accounts = await accountRepo.AllAsync();

        var views = new List<AccountView>(accounts.Count);

        foreach (var account in accounts)
            views.Add(new AccountView(account.Id, account.Name, account.Description, account.Active, TailOf(account.Token)));

        return new ListResponse { Accounts = views };
    }

    private static string TailOf(string token) => token.Length <= Tail ? new string('*', token.Length) : "..." + token[^Tail..];
}
