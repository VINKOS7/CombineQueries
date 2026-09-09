using Newtonsoft.Json;

namespace CombineQueries.Api.Services.Outbox;

// Один доставленный адрес: что просили и что пришло снаружи.
public record Delivery(
    [property: JsonProperty("url")] string Url,
    [property: JsonProperty("response")] string Response,
    [property: JsonProperty("elapsedMs")] long ElapsedMs);

// Почтовый ящик отложенных ответов.
//
// Форвард наружу занимает секунды, а бывает и упирается в таймаут - держать на нём клиента значит
// морозить игрока. Поэтому запрос уходит В ФОН, а отвечаем тем, что уже готово. Недоспевшее
// приезжает довеском к ЛЮБОМУ следующему ответу, каким бы эндпоинтом он ни был.
public interface IOutbox
{
    // Отправляет за адресом в фоне. Возврата ждать не нужно - результат ляжет в ящик.
    void Fetch(string url);

    // Забирает всё, что доспело к этому моменту. Забранное из ящика уходит.
    IReadOnlyList<Delivery> Take();

    // Сколько адресов ещё в полёте: клиенту полезно знать, что довесок будет.
    int Pending { get; }
}
