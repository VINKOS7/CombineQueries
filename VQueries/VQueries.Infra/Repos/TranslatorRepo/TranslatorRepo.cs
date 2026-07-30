using Microsoft.EntityFrameworkCore;
using Dotseed.Domain;

using CombineQueries.Domain.Aggregates.Translator;

namespace CombineQueries.Infra.Repos.TranslatorRepo;

public class TranslatorRepo : ITranslatorRepo
{
    private readonly Context _db;

    public TranslatorRepo(Context db) => _db = db;

    public IUnitOfWork UnitOfWork => _db;

    public async Task AddAsync(Translator translator) => await _db.Translators.AddAsync(translator);

    public async Task<Guid> GetIdByAlphabetAsync(string alphabet) => _db.Translators.FirstAsync(t => t.Alphabet == alphabet).Result.Id;
}
