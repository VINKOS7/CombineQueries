using System.Text.Json;
using System.Text.Json.Nodes;

using NUnit.Framework;

using CombineQueries.Tests.Wire;

namespace CombineQueries.Tests;

// Синтетика Infinite по дереву: строка за потолком L3 едет финитным предком (VF) и номером среди
// его Inf-потомков (hop). Гонять с маленькими размерами, иначе до Inf не дойти: CQ_DFA=16
// CQ_PAGES=8 - финитных адресов 128, всё выученное дальше - Infinite.
//
// Первый проход учит словарь, второй шлёт НОВЫЕ номера: собранные адреса становятся гиперами, и
// повтор тех же ушёл бы прыжками, до фрагментов не дойдя. Новые собираются из выученных кусков.
// Тела сверяются после каждого прохода: Inf, собранный не тем потомком, дал бы чужое тело.
[TestFixture, Order(2)]
public class InfiniteTests
{
    private const string Prefix = "https://dummyjson.com/";

    // Раздел и сколько в нём адресов. Номера берутся только незнакомые клиенту: известный адрес ушёл бы
    // прыжком, до фрагментов не дойдя, - а база мира копит хайперы от прогона к прогону, и прежние
    // 1..40 давно все в ней.
    private static readonly (string Path, int Count)[] Paths =
        [("products", 194), ("users", 208), ("posts", 251), ("carts", 50), ("todos", 254), ("comments", 340), ("quotes", 1454), ("recipes", 50)];

    private const int PerPath = 20;

    [Test]
    public async Task InfiniteGoesByAncestorAndHop()
    {
        using var http = new HttpClient
        {
            BaseAddress = new Uri(Environment.GetEnvironmentVariable("CQ_HOST") ?? "http://localhost:5017"),
            Timeout = TimeSpan.FromSeconds(90)
        };

        var client = new WireClient(http, Token(), TimeSpan.Zero, false);

        await client.ConnectAsync();

        try
        {
            TestContext.Out.WriteLine($"connect: {client.Fragments} фрагментов, из них Inf с адресом {client.Infinite}");

            await PassAsync(client, 1);
            await PassAsync(client, 2);

            Assert.That(client.Infinite, Is.GreaterThan(0), "сервер не дал ни одного адреса Inf - размеры не маленькие?");
            Assert.That(client.HopsSent, Is.GreaterThan(0), "ни один Inf не уехал предком и номером");
        }
        finally
        {
            client.Release();
        }
    }

    private static async Task PassAsync(WireClient client, int pass)
    {
        string[] urls = [.. Paths.SelectMany(path => Enumerable.Range(1, path.Count)
            .Select(number => $"{Prefix}{path.Path}/{number}")
            .Where(url => client.JumpOf(url) < 0)
            .OrderBy(_ => Random.Shared.Next())
            .Take(PerPath))];

        Assert.That(urls, Is.Not.Empty, $"проход {pass}: незнакомых адресов не осталось");

        int queries = client.TotalQueries, hops = client.HopsSent;

        await client.RunAsync(urls, () => { });

        TestContext.Out.WriteLine($"проход {pass}: {urls.Length} адресов, {client.TotalQueries - queries} query, hop {client.HopsSent - hops}, "
            + $"фрагментов {client.Fragments}, Inf с адресом {client.Infinite}");

        Assert.Multiple(() =>
        {
            foreach (string url in urls)
            {
                string body = client.BodyOf(url);

                Assert.That(body, Is.Not.Empty, $"{url}: тело не приехало");

                if (body != "") Assert.That(IdOf(body), Is.EqualTo(int.Parse(url[(url.LastIndexOf('/') + 1)..])), $"{url}: чужое тело");
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
