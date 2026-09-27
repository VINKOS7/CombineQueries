using Newtonsoft.Json;

using CombineQueries.Api.Services.Speech;

namespace CombineQueries.Api.Controllers.Accounts;

// Ответ, который может увезти НОВОЕ кольцо частей токена. Кольцо выдаётся тем же ответом, в котором
// клиент потратил последнюю часть старого: подслушавший старое целиком получает его уже мёртвым.
//
// Пустое поле - обычный случай, кольцо ещё не кончилось, и клиент оставляет своё.
public interface ISigned
{
    [JsonProperty("signs")] string? Signs { get; set; }
}

public static class SignedAnswer
{
    // Ответ подписанного запроса увозит свежее кольцо, если это запрос потратил последнюю часть старого.
    public static TAnswer Signed<TAnswer>(this TAnswer answer, ISpeech speech) where TAnswer : ISigned
    {
        answer.Signs = speech.TakeFreshSigns();

        return answer;
    }
}
