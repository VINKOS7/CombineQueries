using System.Text.Json;
using System.Text.Json.Nodes;

using NUnit.Framework;

namespace CombineQueries.Tests.Wire;

// Подключения на прогон. Сервер держит восемь потоков, и место освобождает только поток, молчавший
// десять минут, - connect на каждый тест выедал их за один прогон, и хвост набора ловил 403
// «You should await when some master instance be closed». Поэтому поток, отпущенный тестом, ждёт
// здесь следующего, и тот берёт его без нового connect.
//
// Свежий connect нужен, только когда тест меняет контекст сервера (алфавит, схему, lengthBatch):
// контекст у сервера один на всех, и ставит его последний connect.
public static class Streams
{
    // Поток, молчавший дольше, сервер уже мог отдать другому - такой не выдаём.
    private static readonly TimeSpan Life = TimeSpan.FromMinutes(9);

    private static readonly object Gate = new();
    private static readonly List<Lease> Free = [];

    public sealed class Lease(JsonNode answer)
    {
        // Ответ connect: корни, словарь, прыжки. Клиенту, взявшему поток повторно, хватает и старого -
        // новое он доучит по ответам.
        public JsonNode Answer { get; } = answer;

        public string Signs { get; set; } = answer["signs"]?.GetValue<string>() ?? "";

        public int Position { get; set; }

        public DateTime Seen { get; set; } = DateTime.UtcNow;
    }

    public static string Query(string alphabetEncoded, string scheme, string token, bool hypers, int? lengthBatch = null, string baseQuery = "vink0s.com") =>
        $"/connect?alphabet={alphabetEncoded}&baseQuery={baseQuery}&runeSize=3&scheme={scheme}&token={token}"
        + $"&dfaSize={Environment.GetEnvironmentVariable("CQ_DFA") ?? "1024"}&pageCount={Environment.GetEnvironmentVariable("CQ_PAGES") ?? "64"}"
        + $"&hopCount=64&rememberInfinite=true&resetHypers=false&hypers={(hypers ? "on" : "off")}"
        + (lengthBatch is null ? "" : $"&lengthBatch={lengthBatch}");

    // Свободный поток, а нет его - новый connect запросом query.
    public static async Task<Lease> TakeAsync(HttpClient http, string query)
    {
        Lease? lease = null;

        lock (Gate)
        {
            Free.RemoveAll(free => DateTime.UtcNow - free.Seen > Life);

            if (Free.Count > 0)
            {
                lease = Free[^1];
                Free.RemoveAt(Free.Count - 1);
            }
        }

        if (lease is null) return await ConnectAsync(http, query);

        await DrainAsync(http, lease);

        return lease;
    }

    // Новый поток в обход свободных: тесту нужен контекст, которого connect ещё не ставил.
    public static async Task<Lease> ConnectAsync(HttpClient http, string query)
    {
        using var response = await http.GetAsync(query);

        string body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"connect -> {(int)response.StatusCode}: {body}");

        return new Lease(JsonNode.Parse(body) ?? new JsonObject());
    }

    public static void Give(Lease? lease)
    {
        if (lease is null) return;

        lease.Seen = DateTime.UtcNow;

        lock (Gate) Free.Add(lease);
    }

    // Долги прошлого хозяина: тела, за которыми он посылал, но не забрал. Достались бы новому, и тот
    // счёл бы их чужими.
    private static async Task DrainAsync(HttpClient http, Lease lease)
    {
        for (int round = 0; round < 20 && lease.Signs.Length > 0; round++)
        {
            char sign = lease.Signs[lease.Position];

            lease.Position = (lease.Position + 1) % lease.Signs.Length;

            using var response = await http.GetAsync($"/tc/{sign}");

            if (!response.IsSuccessStatusCode) continue;

            var answer = JsonNode.Parse(await response.Content.ReadAsStringAsync());

            string fresh = answer?["signs"]?.GetValue<string>() ?? "";

            if (fresh != "" && fresh != lease.Signs) { lease.Signs = fresh; lease.Position = 0; }

            int ready = (answer?["ready"] as JsonArray)?.Count ?? 0;
            int pending = answer?["pending"]?.GetValue<int>() ?? 0;

            if (ready == 0 && pending == 0) break;

            if (pending > 0) await Task.Delay(100);
        }

        lease.Seen = DateTime.UtcNow;
    }

    // Токен мира: из окружения, иначе тот же, что в конфиге сервера рядом.
    public static string Token()
    {
        string? token = Environment.GetEnvironmentVariable("CQ_TOKEN");

        if (!string.IsNullOrEmpty(token)) return token;

        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

        for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); directory is not null; directory = directory.Parent)
        {
            string settings = Path.Combine(directory.FullName, "VQueries", "appsettings.json");

            if (File.Exists(settings)) return JsonNode.Parse(File.ReadAllText(settings), documentOptions: options)?["Auth"]?["Token"]?.GetValue<string>() ?? "";
        }

        return "";
    }
}
