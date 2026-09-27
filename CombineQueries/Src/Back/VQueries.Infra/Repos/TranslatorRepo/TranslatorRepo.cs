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
    //
    // Раздельными запросами: три коллекции одним JOIN перемножаются - словарь × хайперы × цепочки.
    // Пока хайперов не было, это было незаметно; при 1103 × 11 × 109 выходило 1,3 млн строк и
    // девять секунд локально, а на хосте запрос упирался в таймаут, и connect молча уходил в память.
    public async Task<Translator?> GetByAlphabetAsync(string alphabet) =>
        await _db.Translators
            .Include(t => t.VirtualFragments)
            .Include(t => t.Hypers)
            .Include(t => t.Chains)
            .AsSplitQuery()
            .FirstOrDefaultAsync(t => t.Alphabet == alphabet);
}
