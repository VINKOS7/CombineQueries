using MediatR;
using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Hyper;

public record HyperRequest : IRequest<HyperResponse>
{
    [JsonProperty("handle")] public int Value { get; set; }

    // Сколько адресов брать, начиная с Value. Пачка идёт ДИАПАЗОНОМ, а не перечислением: перечислять
    // номера в адресе нельзя - печётся каждое сочетание, и два произвольных номера дают 4096^2
    // ссылок. Диапазон стоит 4096 * count, то есть растёт линейно.
    //
    // Работает потому, что номера выдаются ПО ПОРЯДКУ: адреса, собранные подряд, лежат рядом.
    [JsonProperty("count")] public int Count { get; set; } = 1;

    // Очередная подпись из общего с хвостом кольца, но одним битом - см. Speech.CheckSign.
    [JsonProperty("sign")] public int Sign { get; set; }
}
