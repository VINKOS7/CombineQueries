namespace CombineQueries.Api.Services.Forwarder;

// Итог форвардинга. Ok=false - запроса не случилось вовсе (хост недоступен, таймаут, битый URI):
// это не то же самое, что "целевой ресурс ответил 404", и клиенту эти случаи различать полезно.
// ElapsedMs - чистое время похода наружу, без нашей обвязки: по нему видно, кто тормозит.
public record ForwardResult(bool Ok, int Status, string Body, long ElapsedMs, string? Error);

// Зачем интерфейс: собранную ссылку надо куда-то отправить, но хендлерам всё равно куда именно.
// Им нужен глагол "сходи по URL и принеси тело", а не HttpClient со всей его конфигурацией.
public interface IForwarder
{
    Task<ForwardResult> GetAsync(string url, CancellationToken cancellationToken);
}
