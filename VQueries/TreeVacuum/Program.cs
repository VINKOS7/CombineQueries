Console.OutputEncoding = System.Text.Encoding.UTF8;

static void RunDemo(string alphabet, string label, ArenaTree clientTree, ArenaTree serverTree, string message)
{
    Console.WriteLine($"=== {label} ===");
    Console.WriteLine($"Original message (client wants to send): \"{message}\" ({message.Length} chrs)");
    Console.WriteLine();

    var client = new Client(alphabet, clientTree, message);
    var server = new Server(alphabet, serverTree);

    server.ProcessAll(client);

    bool ok = server.Accumulated == message;

    Console.WriteLine();
    Console.WriteLine($"  Server reassembled from all rounds: \"{server.Accumulated}\"");
    Console.WriteLine($"  Matches original: {(ok ? "YES" : "NO, MISMATCH")}");
    Console.WriteLine($"  Tree nodes: client={client.TreeNodeCount}, server={server.TreeNodeCount} (must match if in sync)");
    Console.WriteLine();
    Console.WriteLine(new string('-', 60));
    Console.WriteLine();
}

string message = "robawsvirobawsvi";

string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789-._~:/?#[]@!$&'()*+,;=%";

var clientTree1 = ArenaTreeFactory.ATRFrom(alphabet);
var serverTree1 = ArenaTreeFactory.ATRFrom(alphabet);

RunDemo(alphabet, "fresh tree, 1st pass", clientTree1, serverTree1, message);
RunDemo(alphabet, "same tree, 2nd pass (repeat)", clientTree1, serverTree1, message);

var clientTree2 = ArenaTreeFactory.ATRFrom(alphabet);
var serverTree2 = ArenaTreeFactory.ATRFrom(alphabet);

RunDemo(alphabet, "shared domain prefix, message 1", clientTree2, serverTree2, "https://vink0s.com/api/users/1");
RunDemo(alphabet, "shared domain prefix, message 2", clientTree2, serverTree2, "https://vink0s.com/api/users/2");
RunDemo(alphabet, "shared domain prefix, message 3", clientTree2, serverTree2, "https://vink0s.com/api/orders/55");
