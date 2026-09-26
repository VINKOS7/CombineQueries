using System.Collections.Concurrent;

using CombineQueries.Api.Services.Forwarder;

namespace CombineQueries.Api.Services.Outbox;

// Ящик живёт синглтоном: он общий для всех запросов, ведь довесок цепляется к любому ответу.
//
// Форвард гоняем в своём scope: IForward это typed HttpClient, а фоновая задача переживает запрос,
// в котором её завели, - брать его зависимости оттуда нельзя.
public class Outbox(IServiceScopeFactory scopes, ILogger<Outbox> logger) : IOutbox
{
    // Очередь и счётчик - на каждый поток: тела ждёт тот, кто за ними послал.
    private readonly ConcurrentDictionary<int, ConcurrentQueue<Delivery>> _ready = new();

    private readonly ConcurrentDictionary<int, int> _pending = new();

    public int Pending(int stream) => _pending.TryGetValue(stream, out int flying) ? flying : 0;

    public void Fetch(string url, int stream)
    {
        _pending.AddOrUpdate(stream, 1, (_, flying) => flying + 1);

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopes.CreateScope();

                var forwarder = scope.ServiceProvider.GetRequiredService<IForward>();
                var result = await forwarder.GetAsync(url, CancellationToken.None);

                Keep(stream, new Delivery(url, result.Body, result.ElapsedMs));
            }
            catch (Exception error)
            {
                // Наружу не пробрасываем: запрос клиента давно закрыт, ронять фон некому.
                // Пустое тело - честный ответ «сходили, не принесли».
                logger.LogWarning("outbox: {Url} failed ({Kind}: {Message})", url, error.GetType().Name, error.Message);

                Keep(stream, new Delivery(url, "", 0));
            }
            finally
            {
                _pending.AddOrUpdate(stream, 0, (_, flying) => flying > 0 ? flying - 1 : 0);
            }
        });
    }

    private void Keep(int stream, Delivery delivery) =>
        _ready.GetOrAdd(stream, _ => new ConcurrentQueue<Delivery>()).Enqueue(delivery);

    public IReadOnlyList<Delivery> Take(int stream)
    {
        if (!_ready.TryGetValue(stream, out var ready) || ready.IsEmpty) return [];

        var taken = new List<Delivery>();

        while (ready.TryDequeue(out var delivery)) taken.Add(delivery);

        return taken;
    }
}
