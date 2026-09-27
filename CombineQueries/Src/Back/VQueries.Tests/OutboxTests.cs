using System.Text.Json;
using System.Text.Json.Nodes;

using NUnit.Framework;

using CombineQueries.Tests.Wire;

namespace CombineQueries.Tests;

// Долги разделены по клиентам. Сервер ходит наружу в фоне, и тело кладёт в ящик ТОГО потока, который
// за ним послал. Общий ящик отдавал тело тому, кто первым спросил: чужой клиент его выбрасывал -
// коробки под этот адрес у него нет, - а хозяин ждал вечно. Так и зависал риг на втором клиенте.
//
// Сервер берётся из окружения: CQ_HOST (по умолчанию http://localhost:5017), CQ_TOKEN (по умолчанию
// Auth:Token из appsettings.json сервера).
[TestFixture, Category("Wire"), NonParallelizable]
public class OutboxTests
{
    private const string AlphabetEncoded = "abcdefghijklmnopqrstuvwxyz0123456789-._~%3A%2F%3F%23%5B%5D%40%21%24%26%27%28%29%2A%2B%2C%3B%3D%25";
    private const string Asked = "https://dummyjson.com/products/3";

    private HttpClient _http = null!;

    [OneTimeSetUp]
    public void Open() => _http = new HttpClient
    {
        BaseAddress = new Uri(Environment.GetEnvironmentVariable("CQ_HOST") ?? "http://localhost:5017"),
        Timeout = TimeSpan.FromSeconds(60)
    };

    [OneTimeTearDown]
    public void Close() => _http.Dispose();

    [Test]
    public async Task ForeignClientDoesNotTakeTheBody()
    {
        // Первый просит адрес по-настоящему, как мир.
        var mine = new WireClient(_http, Token(), TimeSpan.Zero, hypers: false);

        await mine.ConnectAsync();

        // Второй только подключён и всё это время добирает долг - чужой ему доставаться не должен.
        string ring = await ConnectAsync();

        int position = 0;
        var stolen = new List<string>();

        await mine.RunAsync([Asked], () =>
        {
            var (status, taken) = CreditAsync(ring[position]).GetAwaiter().GetResult();

            // Отказ значит, что часть забрал сосед с таким же ожиданием: поток уже сдвинут, идём дальше.
            position = (position + 1) % ring.Length;

            if (status == 200) stolen.AddRange(taken);
        });

        Assert.That(stolen, Is.Empty, "чужому клиенту досталось тело: " + string.Join(", ", stolen));
        Assert.That(mine.BodyOf(Asked), Is.Not.Empty, "свой клиент тела не дождался");
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

    // Добор долга чужим клиентом: что он унёс, то и украдено.
    private async Task<(int Status, IReadOnlyList<string> Taken)> CreditAsync(char sign)
    {
        using var response = await _http.GetAsync($"/tc/{sign}");

        string body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode) return ((int)response.StatusCode, []);

        var taken = new List<string>();

        foreach (var item in JsonNode.Parse(body)?["ready"] as JsonArray ?? [])
        {
            string url = item?["url"]?.GetValue<string>() ?? "";

            if (url != "") taken.Add(url);
        }

        return (200, taken);
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
