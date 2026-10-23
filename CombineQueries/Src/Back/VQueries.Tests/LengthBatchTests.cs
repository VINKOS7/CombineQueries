using NUnit.Framework;

using CombineQueries.Tests.Wire;

namespace CombineQueries.Tests;

// lengthBatch в /connect: самый длинный url, который сервер кладёт хайпером в базу. 0 - без потолка,
// отрицательный - ошибка клиента. Отказ идёт ДО места под поток, поэтому слота не съедает; принятые
// connect оставляют свои потоки в общих - следующие тесты возьмут их вместо новых.
//
// Что url длиннее lengthBatch форвардится и ходит прыжком из памяти - см. HyperTickTests: проверять
// это на мировом алфавите значит учить словарь мира синтетикой.
[TestFixture, Category("Wire"), NonParallelizable, Order(1)]
public class LengthBatchTests
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

    [Test]
    public async Task NegativeIsRefused()
    {
        using var response = await _http.GetAsync(Streams.Query(AlphabetEncoded, "https", Streams.Token(), hypers: false, lengthBatch: -1));

        string body = await response.Content.ReadAsStringAsync();

        Assert.That(response.IsSuccessStatusCode, Is.False, $"lengthBatch=-1 принят: {body}");
        Assert.That(body, Does.Contain("lengthBatch"), "отказ не про lengthBatch");
    }

    [TestCase(0)]
    [TestCase(4096)]
    public async Task IsAccepted(int lengthBatch)
    {
        var lease = await Streams.ConnectAsync(_http, Streams.Query(AlphabetEncoded, "https", Streams.Token(), hypers: false, lengthBatch: lengthBatch));

        Assert.That(lease.Signs, Is.Not.Empty, $"lengthBatch={lengthBatch}: connect не отдал кольцо");

        Streams.Give(lease);
    }
}
