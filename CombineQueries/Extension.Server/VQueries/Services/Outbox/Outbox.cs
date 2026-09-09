using System.Collections.Concurrent;

using CombineQueries.Api.Services.Forwarder;

namespace CombineQueries.Api.Services.Outbox;

// Ящик живёт синглтоном: он общий для всех запросов, ведь довесок цепляется к любому ответу.
//
// Форвард гоняем в своём scope: IForward это typed HttpClient, а фоновая задача переживает запрос,
// в котором её завели, - брать его зависимости оттуда нельзя.
public class Outbox(IServiceScopeFactory scopes, ILogger<Outbox> logger) : IOutbox
{
    private readonly ConcurrentQueue<Delivery> _ready = new();

    private int _pending;

    public int Pending => Volatile.Read(ref _pending);

    public void Fetch(string url)
    {
        Interlocked.Increment(ref _pending);

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopes.CreateScope();

                var forwarder = scope.ServiceProvider.GetRequiredService<IForward>();
                var result = await forwarder.GetAsync(url, CancellationToken.None);

                _ready.Enqueue(new Delivery(url, result.Body, result.ElapsedMs));
            }
            catch (Exception error)
            {
                // Наружу не пробрасываем: запрос клиента давно закрыт, ронять фон некому.
                // Пустое тело - честный ответ «сходили, не принесли».
                logger.LogWarning("outbox: {Url} failed ({Kind}: {Message})", url, error.GetType().Name, error.Message);

                _ready.Enqueue(new Delivery(url, "", 0));
            }
            finally
            {
                Interlocked.Decrement(ref _pending);
            }
        });
    }

    public IReadOnlyList<Delivery> Take()
    {
        if (_ready.IsEmpty) return [];

        var taken = new List<Delivery>();

        while (_ready.TryDequeue(out var delivery)) taken.Add(delivery);

        return taken;
    }
}
