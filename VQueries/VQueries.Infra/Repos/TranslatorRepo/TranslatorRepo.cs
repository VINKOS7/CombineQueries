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

    public async Task<Translator> GetByIdAsync(Guid id) => await _db.Translators.FirstAsync(t => t.Id == id);

    public async Task<bool> IsHasAsync(Guid id) => _db.Translators.AnyAsync(t => t.Id == id).Result;

    public async Task<Guid> GetIdByAlphabetAsync(string alphabet) => _db.Translators.FirstAsync(t => t.Alphabet == alphabet).Result.Id;

    public async Task<Translator> GetByAlphabetAsync(string alphabet) => _db.Translators.FirstAsync(t => t.Alphabet == alphabet).Result;
}
