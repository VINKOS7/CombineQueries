using Dotseed.Domain;

namespace CombineQueries.Domain.Aggregates.Translator;

public interface ITranslatorRepo : IRepository<Translator>
{
    Task AddAsync(Translator translator);

    Task<Guid> GetIdByAlphabetAsync(string alphabet);
}
