using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

using NUnit.Framework;

using CombineQueries.Tests.Wire;

namespace CombineQueries.Tests;

// Сценарий красного рига SampleSteps по проводу, без Unity: одно нажатие - пачка из четырёх адресов
// products/N..N+3, после её закрытия пачка из двух. Печатает vrequest/response/vresponse как риг и
// проверяет, что каждый адрес привёз СВОЁ тело.
//
// Сервер берётся из окружения:
//   CQ_HOST        - адрес сервера, по умолчанию локальный http://localhost:5017;
//   CQ_TOKEN       - токен мира, по умолчанию Auth:Token из appsettings.json сервера;
//   CQ_COOLDOWN_MS - пауза SDK между загрузками, 5000;
//   CQ_HYPERS      - on/off, как MemHypers клиента. On копит цепочки в БД сервера.
[TestFixture, Category("Wire"), NonParallelizable]
public class StepsTests
{
    private const string Prefix = "https://dummyjson.com/products/";
    private const int First = 4;
    private const int Second = 2;

    private HttpClient _http = null!;
    private WireClient _client = null!;

    [OneTimeSetUp]
    public async Task Connect()
    {
        // Соединение не переживает паузу SDK: держать его дольше самой паузы значит ловить мёртвый
        // сокет вместо ответа сервера.
        var connection = new SocketsHttpHandler
        {
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(2),
            ConnectTimeout = TimeSpan.FromSeconds(15)
        };

        _http = new HttpClient(connection)
        {
            BaseAddress = new Uri(Environment.GetEnvironmentVariable("CQ_HOST") ?? "http://localhost:5017"),
            Timeout = TimeSpan.FromSeconds(90)
        };

        var cooldown = TimeSpan.FromMilliseconds(int.Parse(Environment.GetEnvironmentVariable("CQ_COOLDOWN_MS") ?? "5000"));

        _client = new WireClient(_http, Token(), cooldown, Environment.GetEnvironmentVariable("CQ_HYPERS") != "off");

        await _client.ConnectAsync();

        TestContext.Out.WriteLine($"connect: {_http.BaseAddress}, {_client.Fragments} фрагментов, {_client.Jumps} прыжков");
    }

    [OneTimeTearDown]
    public void Disconnect() => _http.Dispose();

    // Одно нажатие: обе пачки по очереди, у каждой своё время от своего vrequest.
    [TestCase(7)]
    public async Task Press(int steps)
    {
        int spent = await PressAsync(steps);

        TestContext.Out.WriteLine($"нажатие: {spent} query");
    }

    // Повтор того же нажатия: адреса уже собраны, их цепочки известны - второй раз клиент уходит
    // прыжками и не может потратить больше, чем на первую сборку.
    [TestCase(21)]
    public async Task RepeatGoesByJumps(int steps)
    {
        int first = await PressAsync(steps);
        int again = await PressAsync(steps);

        Assert.That(again, Is.LessThanOrEqualTo(first), $"повтор {again} query против первого нажатия {first}");
    }

    private async Task<int> PressAsync(int steps)
    {
        TestContext.Out.WriteLine($"\n==== нажатие: шагов {steps}");

        int before = _client.TotalQueries;

        await BatchAsync(Enumerable.Range(steps, First));
        await BatchAsync(Enumerable.Range(steps + First, Second));

        return _client.TotalQueries - before;
    }

    private async Task BatchAsync(IEnumerable<int> numbers)
    {
        string[] urls = [.. numbers.Select(number => Prefix + number)];

        var clock = Stopwatch.StartNew();
        int before = _client.TotalQueries;
        var seen = new HashSet<string>();

        TestContext.Out.WriteLine("vrequest: " + string.Join(", ", urls.Select(url => url[Prefix.Length..])));

        await _client.RunAsync(urls, () =>
        {
            foreach (string url in urls)
            {
                string body = _client.BodyOf(url);

                if (body == "" || !seen.Add(url)) continue;

                TestContext.Out.WriteLine($"response: {url}, {body.Length} bytes, {_client.SpentOf(url)} queries, {clock.ElapsedMilliseconds} ms");
            }
        });

        int got = urls.Count(url => _client.BodyOf(url) != "");

        TestContext.Out.WriteLine($"vresponse: {(got == urls.Length ? "полная" : "частичная")} {got}/{urls.Length}, "
            + $"{urls.Sum(url => _client.BodyOf(url).Length)} bytes, {_client.TotalQueries - before} queries, {clock.ElapsedMilliseconds} ms");

        Assert.Multiple(() =>
        {
            foreach (string url in urls)
            {
                string body = _client.BodyOf(url);

                Assert.That(body, Is.Not.Empty, $"{url}: тело не приехало");

                if (body != "") Assert.That(IdOf(body), Is.EqualTo(int.Parse(url[Prefix.Length..])), $"{url}: чужое тело");
            }
        });
    }

    private static int IdOf(string body)
    {
        try
        {
            return JsonNode.Parse(body)?["id"]?.GetValue<int>() ?? -1;
        }
        catch (JsonException)
        {
            return -1;
        }
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
