using CombineQueries.Api.Services.Speech;
using MediatR;
using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translators.Handlers.CombineNew;

public record CombineRequest : IRequest<CombineResponse>
{
    [JsonProperty("runes")] public required string Runes { get; set; }

    // Адрес VF: page (Развязка-2) + id (Развязка-1, offset). Глобальный id = page*DfaSize + id.
    // page=0 -> L2, page>0 -> L3. Всё в одном запросе (отдельный /g/ не нужен).
    [JsonProperty("id")] public int Id { get; set; }
    [JsonProperty("page")] public int Page { get; set; }

    // Сколько единиц С КОНЦА - фрагменты (остальное - руны). single-unit: 0 = чанк, 1 = один VF.
    // Паковка (q>1: n рун + k фрагментов) - второй коммит.
    [JsonProperty("q")] public int Q { get; set; }
}
