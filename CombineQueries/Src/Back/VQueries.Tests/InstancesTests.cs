using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

using NUnit.Framework;

using CombineQueries.Tests.Wire;

namespace CombineQueries.Tests;

// Синтетика многих инстансов: CQ_INSTANCES клиентов (по умолчанию 4), у каждого своё соединение и
// свой поток подписей. Подключаются по очереди, потом РАЗОМ гонят каждый свою пачку свежих адресов.
//
// Ловит смешение потоков: руна и пара идут без подписи и достаются потоку, подписывавшему последним,
// - при нескольких клиентах разом кусок может лечь в чужую сборку. Тогда тело приедет не тому или
// адрес склеится из двух. Номера свежие (случайное окно), иначе адреса уйдут прыжками, мимо сборки.
[TestFixture]
public class InstancesTests
{
    private const string Prefix = "https://dummyjson.com/";

    // Раздел и сколько в нём адресов: окно выбирается внутри.
    private static readonly (string Path, int Count)[] Paths =
        [("quotes", 1454), ("comments", 340), ("todos", 254), ("posts", 251), ("users", 208), ("products", 194)];

    private const int Batch = 6;

    [Test]
    public async Task InstancesDoNotMixStreams()
    {
        int instances = int.Parse(Environment.GetEnvironmentVariable("CQ_INSTANCES") ?? "4");
        var cooldown = TimeSpan.FromMilliseconds(int.Parse(Environment.GetEnvironmentVariable("CQ_COOLDOWN_MS") ?? "0"));
        var host = new Uri(Environment.GetEnvironmentVariable("CQ_HOST") ?? "http://localhost:5017");

        var https = new List<HttpClient>();
        var clients = new List<WireClient>();
        var batches = new List<string[]>();

        try
        {
            for (int i = 0; i < instances; i++)
            {
                var http = new HttpClient { BaseAddress = host, Timeout = TimeSpan.FromSeconds(90) };
                var client = new WireClient(http, Token(), cooldown, true);

                await client.ConnectAsync();

                var (path, count) = Paths[i % Paths.Length];
                int first = Random.Shared.Next(1, count - Batch);

                https.Add(http);
                clients.Add(client);
                batches.Add([.. Enumerable.Range(first, Batch).Select(number => $"{Prefix}{path}/{number}")]);

                TestContext.Out.WriteLine($"инстанс {i}: {path}/{first}..{first + Batch - 1}");
            }

            var clock = Stopwatch.StartNew();
            var spent = new long[instances];

            await Task.WhenAll(clients.Select(async (client, i) =>
            {
                await client.RunAsync(batches[i], () => { });

                spent[i] = clock.ElapsedMilliseconds;
            }));

            for (int i = 0; i < instances; i++)
            {
                var client = clients[i];
                int got = batches[i].Count(url => client.BodyOf(url) != "");

                TestContext.Out.WriteLine($"инстанс {i}: {got}/{Batch} тел, {clients[i].TotalQueries} query, {spent[i]} ms");
            }

            Assert.Multiple(() =>
            {
                for (int i = 0; i < instances; i++)
                    foreach (string url in batches[i])
                    {
                        string body = clients[i].BodyOf(url);

                        Assert.That(body, Is.Not.Empty, $"инстанс {i}, {url}: тело не приехало");

                        if (body != "") Assert.That(IdOf(body), Is.EqualTo(int.Parse(url[(url.LastIndexOf('/') + 1)..])), $"инстанс {i}, {url}: чужое тело");
                    }
            });
        }
        finally
        {
            foreach (var http in https) http.Dispose();
        }
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
