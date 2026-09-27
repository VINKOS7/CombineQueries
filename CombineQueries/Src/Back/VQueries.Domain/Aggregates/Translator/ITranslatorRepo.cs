using Dotseed.Domain;

namespace CombineQueries.Domain.Aggregates.Translator;

public interface ITranslatorRepo : IRepository<Translator>
{
    Task AddAsync(Translator translator);

    Task<Guid> GetIdByAlphabetAsync(string alphabet);

    // Tracked и со словарём: VF меняются только через сам агрегат, значит и грузить его надо целиком.
    Task<Translator?> GetByAlphabetAsync(string alphabet);
}
