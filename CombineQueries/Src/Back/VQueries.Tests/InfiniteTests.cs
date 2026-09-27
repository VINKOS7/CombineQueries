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
[TestFixture]
public class InfiniteTests
{
    private const string Prefix = "https://dummyjson.com/";

    private static readonly string[] Paths = ["products", "users", "posts", "carts", "todos", "comments", "quotes", "recipes"];

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

        TestContext.Out.WriteLine($"connect: {client.Fragments} фрагментов, из них Inf с адресом {client.Infinite}");

        await PassAsync(client, 1, 1);
        await PassAsync(client, 2, 21);

        Assert.That(client.Infinite, Is.GreaterThan(0), "сервер не дал ни одного адреса Inf - размеры не маленькие?");
        Assert.That(client.HopsSent, Is.GreaterThan(0), "ни один Inf не уехал предком и номером");
    }

    private static async Task PassAsync(WireClient client, int pass, int first)
    {
        string[] urls = [.. Paths.SelectMany(path => Enumerable.Range(first, 20).Select(number => $"{Prefix}{path}/{number}"))];

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
