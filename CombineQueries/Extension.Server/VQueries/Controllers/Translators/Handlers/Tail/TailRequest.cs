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

    // Закрывающий кусок: адрес фрагмента, который надо положить в поток ПЕРЕД закрытием. -1 -
    // обычный хвост, куска нет.
    //
    // Ради него и заведён /cf: адрес, целиком накрытый одним куском словаря, стоил два запроса -
    // продиктовать кусок и закрыть. Теперь одно и другое едут вместе.
    [JsonProperty("fragment")] public int Fragment { get; set; } = -1;
}
