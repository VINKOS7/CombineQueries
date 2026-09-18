using System.Text.Json;
using System.Text.Json.Nodes;

using NUnit.Framework;

namespace CombineQueries.Tests;

// Много клиентов на одном сервере. Каждый connect рождает СВОЁ кольцо частей токена, и сервер держит
// по одной ожидаемой части на подключение. Пришедшую часть он ищет среди ожидаемых: нашлась - поток
// сдвигается, не нашлась - запрос отбивается, не роняя остальных.
//
// Частей всего восемь, поэтому двое могут ждать одну и ту же: сервер тогда сдвинет не того, и у
// настоящего владельца часть окажется потрачена. Это принято сознательно, и тест это учитывает -
// отказ по своей части значит, что её забрал сосед, и клиент идёт к следующей.
//
// Кольцо не вечное: на последней части сервер выдаёт новое тем же ответом. Подслушавший старое
// целиком получает его уже мёртвым - это и проверяем.
//
// Сервер берётся из окружения: CQ_HOST (по умолчанию http://localhost:5017), CQ_TOKEN (по умолчанию
// Auth:Token из appsettings.json сервера).
[TestFixture, Category("Wire"), NonParallelizable]
public class SignsTests
{
    private const string AlphabetEncoded = "abcdefghijklmnopqrstuvwxyz0123456789-._~%3A%2F%3F%23%5B%5D%40%21%24%26%27%28%29%2A%2B%2C%3B%3D%25";

    private HttpClient _http = null!;

    [OneTimeSetUp]
    public void Open() => _http = new HttpClient
    {
        BaseAddress = new Uri(Environment.GetEnvironmentVariable("CQ_HOST") ?? "http://localhost:5017"),
        Timeout = TimeSpan.FromSeconds(60)
    };

    [OneTimeTearDown]
    public void Close() => _http.Dispose();

    // Два подключения - два разных кольца, и оба потока живут одновременно.
    [Test]
    public async Task TwoClientsGetTheirOwnRings()
    {
        string first = await ConnectAsync();
        string second = await ConnectAsync();

        Assert.That(first, Is.Not.Empty, "connect не отдал кольцо");
        Assert.That(second, Is.Not.EqualTo(first), "двум подключениям досталось одно кольцо");

        var one = new Ring(this, first);
        var two = new Ring(this, second);

        for (int i = 0; i < 4; i++)
        {
            await one.StepAsync();
            await two.StepAsync();
        }

        Assert.That(one.Taken, Is.GreaterThan(0), "первый клиент не прошёл ни одной части");
        Assert.That(two.Taken, Is.GreaterThan(0), "второй клиент не прошёл ни одной части");
    }

    // Кольцо кончилось - сервер выдал новое тем же ответом, и клиент идёт дальше по нему.
    [Test]
    public async Task RingIsReplacedWhenSpent()
    {
        string signs = await ConnectAsync();

        var ring = new Ring(this, signs);

        // Кольцо в 256 частей: с запасом на те, что заберут соседи по совпавшей части.
        for (int i = 0; i < signs.Length * 2 && ring.Fresh == ""; i++) await ring.StepAsync();

        Assert.That(ring.Fresh, Is.Not.Empty, $"кольцо не обновилось за {ring.Sent} запросов");
        Assert.That(ring.Fresh, Is.Not.EqualTo(signs), "сервер выдал то же самое кольцо");

        Assert.That(await ring.StepAsync(), Is.EqualTo(200), "первая часть нового кольца отбита");
    }

    // Проход по кольцу. Отказ по своей части значит, что её забрал сосед с таким же ожиданием:
    // поток уже сдвинут, поэтому идём к следующей части, а не повторяем эту.
    private sealed class Ring(SignsTests tests, string signs)
    {
        private string _signs = signs;
        private int _position;

        public string Fresh { get; private set; } = "";

        public int Sent { get; private set; }

        public int Taken { get; private set; }

        public async Task<int> StepAsync()
        {
            char part = _signs[_position];

            _position = (_position + 1) % _signs.Length;

            var (status, fresh) = await tests.CreditAsync(part);

            Sent++;

            if (status == 200) Taken++;

            if (fresh == "") return status;

            Fresh = fresh;
            _signs = fresh;
            _position = 0;

            return status;
        }
    }

    private async Task<string> ConnectAsync()
    {
        string query = $"/connect?alphabet={AlphabetEncoded}&baseQuery=vink0s.com&runeSize=3&scheme=https&token={Token()}"
            + "&dfaSize=1024&pageCount=64&hopCount=64&rememberInfinite=true&resetHypers=false&hypers=off";

        using var response = await _http.GetAsync(query);

        string body = await response.Content.ReadAsStringAsync();

        Assert.That((int)response.StatusCode, Is.EqualTo(200), $"connect -> {(int)response.StatusCode}: {body}");

        return JsonNode.Parse(body)?["signs"]?.GetValue<string>() ?? "";
    }

    // Добор долга: самый дешёвый подписанный запрос. Возвращает код и новое кольцо, если оно приехало.
    private async Task<(int Status, string Fresh)> CreditAsync(char sign)
    {
        using var response = await _http.GetAsync($"/tc/{sign}");

        string body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode) return ((int)response.StatusCode, "");

        return (200, JsonNode.Parse(body)?["signs"]?.GetValue<string>() ?? "");
    }

    // Токен мира: из окружения, иначе тот же, что в конфиге сервера рядом.
    private static string Token()
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
