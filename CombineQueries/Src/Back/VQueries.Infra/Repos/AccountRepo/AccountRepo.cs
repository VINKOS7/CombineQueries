using Microsoft.EntityFrameworkCore;
using Dotseed.Domain;

using CombineQueries.Domain.Aggregates.Account;

namespace CombineQueries.Infra.Repos.AccountRepo;

public class AccountRepo : IAccountRepo
{
    private readonly Context _db;

    public AccountRepo(Context db) => _db = db;

    public IUnitOfWork UnitOfWork => _db;

    public async Task AddAsync(Account account) => await _db.Accounts.AddAsync(account);

    public async Task<Guid> GetIdByTokenAsync(string token)
    {
        var account = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Token == token && a.Active);

        return account is null ? Guid.Empty : account.Id;
    }

    // Tracked: возвращаем сам Account, чтобы на нём поднять событие и сохранить (SaveEntitiesAsync).
    public async Task<Account?> GetByTokenAsync(string token) =>
        await _db.Accounts.FirstOrDefaultAsync(a => a.Token == token && a.Active);
}
