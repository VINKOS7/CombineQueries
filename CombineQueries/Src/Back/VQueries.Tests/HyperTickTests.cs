using System.Text.Json.Nodes;

using NUnit.Framework;

using CombineQueries.Tests.Tunnel;

namespace CombineQueries.Tests;

// Сколько данных приносит один гипер-такт: /h с count=8 по выученной семье из восьми братьев.
// Цель форварда - свой Echo на loopback, тело любого размера. Поэтому connect идёт со схемой http, а
// словарь - на отдельном алфавите (Isolated): длинная синтетика иначе осела бы в словаре мира.
//
// Фикстура последняя в прогоне: её connect меняет контекст сервера (схему и алфавит) для всех. Мир
// после неё переподключается сам - на Play.
[TestFixture, Category("Tunnel"), NonParallelizable, Order(8)]
public class HyperTickTests
{
    private static readonly int[] Lengths = [2048, 8192, 16384, 27000];

    // Последнее - больше потолка форвардера в 1 МБ: такое тело обязано прийти пустым.
    private static readonly int[] Sizes = [1024, 100 * 1024, 1000 * 1024, 2 * 1024 * 1024];

    private const int ForwardLimit = 1024 * 1024;
    private const int Family = 8;

    private HttpClient _http = null!;
    private Echo _echo = null!;

    [OneTimeSetUp]
    public void Open()
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(Environment.GetEnvironmentVariable("CQ_HOST") ?? "http://localhost:5017"),
            Timeout = TimeSpan.FromSeconds(120)
        };

        _echo = new Echo();
    }

    [OneTimeTearDown]
    public void Close()
    {
        _echo.Dispose();
        _http.Dispose();
    }

    // url длиннее lengthBatch в базу не идёт, но форвардится и до рестарта ходит прыжком из памяти.
    // Саму базу провод не видит - видно только, что прыжок узнан и тело пришло.
    [Test, Order(1)]
    public async Task OverLengthBatchGoesByHyperFromMemory()
    {
        var client = new TunnelClient(_http, Streams.Token(), TimeSpan.Zero, false) { Scheme = "http", Isolated = true, LengthBatch = 4096 };

        await client.ConnectAsync();

        try
        {
            string url = UrlOf(8192, "over", 0);

            await client.RunAsync([url], () => { });

            int jump = client.JumpOf(url);

            Assert.That(jump, Is.GreaterThanOrEqualTo(0), "длинный url не стал хайпером");

            await client.DrainAsync();

            var answer = await client.HyperAsync(jump, 1);

            await client.DrainAsync();

            Assert.That(SentOf(answer), Does.Contain(url), "прыжок не назвал длинный url");
            Assert.That(client.BodyOf(url), Has.Length.EqualTo(_echo.BodySize), "тело по прыжку не пришло");
            Assert.That(_echo.Paths.Max(), Is.EqualTo(url.Length - ("http://127.0.0.1:" + _echo.Port).Length), "до цели дошёл не весь url");
        }
        finally
        {
            client.Release();
        }
    }

    [Test, Order(2)]
    public async Task MaxDataPerHyperTick()
    {
        var client = new TunnelClient(_http, Streams.Token(), TimeSpan.Zero, false) { Scheme = "http", Isolated = true, LengthBatch = 0 };

        await client.ConnectAsync();

        var rows = new List<string>();

        try
        {
            foreach (int length in Lengths)
            {
                string[] family = [.. Enumerable.Range(0, Family).Select(i => UrlOf(length, "l" + length, i))];

                _echo.BodySize = 1024;

                int before = client.TotalQueries;

                await client.RunAsync(family, () => { });
                await client.DrainAsync();

                int learned = client.TotalQueries - before;
                int first = family.Select(client.JumpOf).Where(jump => jump >= 0).DefaultIfEmpty(-1).Min();

                Assert.That(first, Is.GreaterThanOrEqualTo(0), $"{length}: семья не стала хайперами");

                TestContext.Out.WriteLine($"учёба {length}: {learned} query на {Family} url, прыжки {string.Join(",", family.Select(client.JumpOf))}");

                foreach (int size in Sizes)
                {
                    _echo.BodySize = size;

                    var answer = await client.HyperAsync(first, Family);

                    long ms = client.LastMs;
                    int bytes = client.LastBytes;

                    var sent = SentOf(answer).Where(family.Contains).ToList();
                    var ready = (answer["ready"] as JsonArray ?? []).Where(item => family.Contains(item?["url"]?.GetValue<string>() ?? "")).ToList();
                    long body = ready.Sum(item => (long)(item?["response"]?.GetValue<string>()?.Length ?? 0));
                    int pending = answer["pending"]?.GetValue<int>() ?? 0;

                    rows.Add($"| {length,6} | {Kb(size),8} | {sent.Count,4} | {ready.Count,5} | {pending,7} | {Kb(body),9} | {Kb(bytes),9} | {ms,6} |");

                    if (size > ForwardLimit)
                        Assert.That(ready.Select(item => item?["response"]?.GetValue<string>() ?? ""), Is.All.Empty, $"{length} x {Kb(size)}: тело выше 1 МБ пришло непустым");
                    else
                        Assert.That(ready.Select(item => item?["response"]?.GetValue<string>()?.Length ?? 0), Is.All.EqualTo(size), $"{length} x {Kb(size)}: тело пришло не того размера");

                    Assert.That(sent, Is.Not.Empty, $"{length} x {Kb(size)}: прыжок не назвал ни одного брата");

                    await client.DrainAsync();
                }
            }
        }
        finally
        {
            client.Release();

            TestContext.Out.WriteLine("\n| url, симв | тело | sent | ready | pending | тел вниз | ответ /h | мс |");
            TestContext.Out.WriteLine("|---|---|---|---|---|---|---|---|");

            foreach (string row in rows) TestContext.Out.WriteLine(row);
        }
    }

    // Братья: общее всё, кроме последнего шага. Длина - полная, со схемой.
    private string UrlOf(int length, string name, int brother)
    {
        string head = $"http://127.0.0.1:{_echo.Port}/{name}/";
        string tail = $"/s{brother}";

        return head + new string('a', length - head.Length - tail.Length) + tail;
    }

    private static List<string> SentOf(JsonNode answer) =>
        [.. (answer["sent"] as JsonArray ?? []).Select(item => item?["url"]?.GetValue<string>() ?? "")];

    private static string Kb(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024.0:0.##} МБ" : $"{bytes / 1024.0:0.#} КБ";
}
