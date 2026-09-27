using MediatR;

using Newtonsoft.Json;

using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Controllers.Translators.Handlers.Tail;

public record TailRequest : IRequest<TailResponse>
{
    [JsonProperty("runes")] public required string Runes { get; set; }

    [JsonProperty("type")] public TypeQuery Type { get; set; }

    // Очередная подпись из выданной на connect последовательности.
    [JsonProperty("sign")] public int Sign { get; set; }

    // Закрывающий кусок: глобальный номер фрагмента, который надо положить в поток ПЕРЕД закрытием.
    // -1 - обычный хвост, куска нет.
    //
    // Ради него и заведён /sf: адрес, целиком накрытый одним куском словаря, стоил два запроса -
    // продиктовать кусок и закрыть. Теперь одно и другое едут вместе.
    [JsonProperty("fragment")] public int Fragment { get; set; } = -1;

    // Флаг склейки. true - запрос закрывает цепочку, накопленные куски её. false - адрес весь в
    // этом запросе, и всё накопленное чужое: осталось от потерянной цепочки.
    [JsonProperty("merge")] public bool Merge { get; set; } = true;

    // Одиночная руна: первая руна адреса без метки «:» - весь адрес в ней. Кладётся в поток перед
    // закрытием, как Fragment у /sf. Пусто - руны нет.
    [JsonProperty("rune")] public string Rune { get; set; } = "";

    // Фрагмент и одна-две буквы одной ссылкой: весь адрес в первой /c, без подписи. Fragment - его номер,
    // Runes - руна с буквой.
    [JsonProperty("pair")] public bool Pair { get; set; }
}
