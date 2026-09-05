Console.OutputEncoding = System.Text.Encoding.UTF8;

string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789-._~:/?#[]@!$&'()*+,;=%";

// одно и то же сообщение шлём N раз подряд на ОДНОМ дереве (дерево прогревается)
static void WarmupTable(string alphabet, string message, int passes)
{
    Console.WriteLine($"Сообщение: \"{message}\" ({message.Length} символов), шлём {passes} раз подряд на одном дереве");
    Console.WriteLine();
    Console.WriteLine("проход | запросов | wire-символов | ratio | узлов в дереве");
    Console.WriteLine("-------|----------|---------------|-------|---------------");

    var clientTree = ArenaTreeFactory.ATRFrom(alphabet);
    var serverTree = ArenaTreeFactory.ATRFrom(alphabet);
    var server = new Server(alphabet, serverTree);

    for (int p = 1; p <= passes; p++)
    {
        var client = new Client(alphabet, clientTree, message);
        server.ProcessAll(client, verbose: false);

        double ratio = (double)message.Length / server.LastWireChars;
        bool ok = server.Accumulated == message;

        Console.WriteLine($"{p,6} | {server.LastRounds,8} | {server.LastWireChars,13} | {ratio,4:F2}x | {server.TreeNodeCount,6}{(ok ? "" : "  !!! MISMATCH")}");
    }

    Console.WriteLine();
}

WarmupTable(alphabet, "https://vink0s.com/api/users/1", 10);
WarmupTable(alphabet, "robawsvirobawsvi", 10);

Console.WriteLine("=== сценарий 2 на реальном домене: когда переключаться на дерево ===");
Console.WriteLine();

DomainWarmup.Run(alphabet, "https://vink0s.com/api/users/1", 12);
DomainWarmup.Run(alphabet, "robawsvirobawsvi", 12);
