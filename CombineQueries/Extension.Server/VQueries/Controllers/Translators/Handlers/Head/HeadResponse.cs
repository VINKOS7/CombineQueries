using Newtonsoft.Json;

using CombineQueries.Api.Services.Outbox;


namespace CombineQueries.Api.Controllers.Translators.Handlers.Head;

// Найденный адрес и номер его цепочки.
public record Found(
    [property: JsonProperty("url")] string Url,
    [property: JsonProperty("jump")] int Jump);

public record HeadResponse
{
    // Нашлись ли адреса с этим куском. false - клиент диктует свой url обычной дорогой.
    [JsonProperty("known")] public bool Known { get; set; }

    // Что нашлось: адрес и его номер. Тел здесь нет - наружу сервер ходит в фон, тела приезжают
    // ДОЛГОМ. Клиент кладёт эти пары в своё кольцо и дальше ходит по ним прыжками.
    [JsonProperty("found")] public IReadOnlyList<Found>? Found { get; set; }

    [JsonProperty("urls")] public int Urls { get; set; }

    // Кусок, который сервер придержал после промаха: он приклеится концом адреса при закрытии.
    // Клиенту это команда «диктуй только начало» - конец у сервера уже есть, за него заплачено.
    //
    // Словаря в довесок тут нет намеренно: кусок клиент назвал АДРЕСОМ из своего словаря, значит
    // про его фрагментацию он и так знает. Слать ему обратно то, что он сам прислал, незачем.
    [JsonProperty("kept")] public string? Kept { get; set; }

    // Доспевшее к этому моменту, довеском к любому ответу.
    [JsonProperty("ready")] public IReadOnlyList<Delivery>? Ready { get; set; }

    [JsonProperty("pending")] public int Pending { get; set; }

    [JsonProperty("elapsedMs")] public long ElapsedMs { get; set; }
}
