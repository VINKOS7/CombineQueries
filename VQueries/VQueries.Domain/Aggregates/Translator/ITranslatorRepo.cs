using Dotseed.Domain;

namespace CombineQueries.Domain.Aggregates.Translator;

public interface ITranslatorRepo : IRepository<Translator>
{
    Task AddAsync(Translator translator);

    Task<bool> IsHasAsync(Guid id);

    Task<Guid> GetIdByAlphabetAsync(string alphabet);

    Task<Translator> GetByAlphabetAsync(string alphabet);
}
