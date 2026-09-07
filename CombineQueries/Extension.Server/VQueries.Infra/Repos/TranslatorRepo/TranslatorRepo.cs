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

    public async Task<Guid> GetIdByAlphabetAsync(string alphabet)
    {
        var translator = await _db.Translators.AsNoTracking().FirstOrDefaultAsync(t => t.Alphabet == alphabet);

        return translator is null ? Guid.Empty : translator.Id;
    }

    // Tracked (без AsNoTracking) и с Include: словарь и хайперы правятся через агрегат, значит
    // должны отслеживаться. Грузим агрегат целиком - он и есть граница транзакции.
    public async Task<Translator?> GetByAlphabetAsync(string alphabet) =>
        await _db.Translators
            .Include(t => t.VirtualFragments)
            .Include(t => t.Hypers)
            .Include(t => t.Chains)
            .FirstOrDefaultAsync(t => t.Alphabet == alphabet);
}
