using Newtonsoft.Json;

namespace CombineQueries.Api.Controllers.Accounts;

// Ответ, который может увезти НОВОЕ кольцо частей токена. Кольцо выдаётся тем же ответом, в котором
// клиент потратил последнюю часть старого: подслушавший старое целиком получает его уже мёртвым.
//
// Пустое поле - обычный случай, кольцо ещё не кончилось, и клиент оставляет своё.
public interface ISigned
{
    [JsonProperty("signs")] string? Signs { get; set; }
}
