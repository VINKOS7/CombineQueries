using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CombineQueries.Tests.Tunnel;

// Цель форварда для замеров: отвечает телом заданного размера на любой путь. Голый TCP, а не
// HttpListener: у http.sys потолок строки запроса ~16 КБ и сегмента 260 символов, а меряем url
// до 27 000 символов - он упёрся бы в Windows раньше, чем в сервер.
public sealed class Echo : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();

    // Длины путей, дошедших до цели: доказательство, что длинный url приехал целиком.
    public ConcurrentQueue<int> Paths { get; } = new();

    // Размер тела следующих ответов, в байтах.
    public int BodySize { get; set; } = 1024;

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public Echo()
    {
        _listener.Start();

        _ = Task.Run(AcceptAsync);
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;

            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            _ = Task.Run(() => AnswerAsync(client));
        }
    }

    private async Task AnswerAsync(TcpClient client)
    {
        using var _ = client;

        try
        {
            var stream = client.GetStream();
            var head = new StringBuilder();
            var buffer = new byte[8192];

            // Тела у GET нет: запрос кончается пустой строкой после заголовков.
            while (!head.ToString().Contains("\r\n\r\n"))
            {
                int read = await stream.ReadAsync(buffer, _stop.Token);

                if (read == 0) return;

                head.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }

            string line = head.ToString()[..head.ToString().IndexOf("\r\n", StringComparison.Ordinal)];
            string[] parts = line.Split(' ');

            Paths.Enqueue(parts.Length > 1 ? parts[1].Length : 0);

            byte[] body = Encoding.ASCII.GetBytes(new string('x', BodySize));
            byte[] header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");

            await stream.WriteAsync(header, _stop.Token);
            await stream.WriteAsync(body, _stop.Token);
        }
        catch (Exception broken) when (broken is IOException or SocketException or OperationCanceledException)
        {
            // Форвардер бросил запрос (таймаут, потолок тела) - отвечать уже некому.
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }
}
