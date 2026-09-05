using Dotseed.Domain;

namespace CombineQueries.Domain.Aggregates.Account;

public interface IAccountRepo : IRepository<Account>
{
    Task AddAsync(Account account);

    Task<Guid> GetIdByTokenAsync(string token);
}
