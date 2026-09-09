// Dev-дамп: что РЕАЛЬНО лежит в базе. Нужен потому, что в dev гиперизация живёт в ОЗУ сервера
// (hypers=off), и глазами отличить «сервер помнит» от «сохранено» иначе нельзя.
//
// Запуск:
//   dotnet run --project VQueries.Dump              - всё сразу
//   dotnet run --project VQueries.Dump -- chains    - только дерево цепочек
//   dotnet run --project VQueries.Dump -- dict      - только словарь
//
// Строку подключения берём из appsettings.Development.json сервера и НЕ печатаем.

using System.Text.Json;
using Npgsql;

string root = AppContext.BaseDirectory;
string? config = null;

// Ищем конфиг сервера вверх по дереву: dotnet run зовут и из корня решения, и из папки проекта.
for (var dir = new DirectoryInfo(root); dir is not null && config is null; dir = dir.Parent)
{
    string candidate = Path.Combine(dir.FullName, "VQueries", "appsettings.Development.json");

    if (File.Exists(candidate)) config = candidate;
}

if (config is null)
{
    Console.WriteLine("не нашёл VQueries/appsettings.Development.json - запусти из папки решения");

    return 1;
}

using var doc = JsonDocument.Parse(File.ReadAllText(config));

if (!doc.RootElement.TryGetProperty("ConnectionStrings", out var strings) ||
    !strings.TryGetProperty("Context", out var context) ||
    context.GetString() is not string connection)
{
    Console.WriteLine("в конфиге нет ConnectionStrings:Context");

    return 1;
}

string what = args.Length > 0 ? args[0] : "all";

await using var db = new NpgsqlDataSourceBuilder(connection).Build();

// Словарь нужен и сам по себе, и чтобы расшифровать шаги цепочек: "f18" - это адрес фрагмента.
var fragments = new Dictionary<int, (string Text, int Level)>();

await using (var cmd = db.CreateCommand(@"select ""Id"", ""Text"", ""Level"" from ""VirtualFragments"""))
await using (var reader = await cmd.ExecuteReaderAsync())
    while (await reader.ReadAsync())
        fragments[reader.GetInt32(0)] = (reader.GetString(1), reader.GetInt32(2));

if (what is "all" or "dict") await Dictionary();
if (what is "all" or "chains") await Chains();
if (what is "all" or "hypers") await Hypers();
if (what == "candidates") await Candidates();

return 0;

// Сколько адресов найдётся по одному куску. Это и есть цена вопроса для /head: если кандидатов
// единицы, обрезок базы не нужен вовсе; если десятки - без него не обойтись.
async Task Candidates()
{
    var urls = new List<string>();

    await using (var cmd = db.CreateCommand(@"select ""Url"" from ""Chains"" where ""Url"" is not null"))
    await using (var reader = await cmd.ExecuteReaderAsync())
        while (await reader.ReadAsync()) urls.Add(reader.GetString(0));

    Console.WriteLine();
    Console.WriteLine($"=== кандидаты по одному куску ({urls.Count} адресов в дереве) ===");

    // Берём куски так же, как их берёт клиент: самый длинный фрагмент, стоящий в адресе.
    var counts = new List<(string Text, int Hits)>();

    foreach (var (id, fragment) in fragments)
    {
        if (fragment.Text.Length < 4) continue;

        int hits = 0;

        foreach (string url in urls) if (url.Contains(fragment.Text, StringComparison.Ordinal)) hits++;

        if (hits > 0) counts.Add((fragment.Text, hits));
    }

    counts.Sort((a, b) => b.Hits.CompareTo(a.Hits));

    Console.WriteLine($"  кусков, встречающихся хоть в одном адресе: {counts.Count}");
    Console.WriteLine();
    Console.WriteLine("  худшие (самые общие куски):");

    foreach (var (text, hits) in counts.Take(5)) Console.WriteLine($"    {hits,5}  {text}");

    Console.WriteLine();
    Console.WriteLine("  лучшие (различающие куски):");

    foreach (var (text, hits) in counts.TakeLast(5)) Console.WriteLine($"    {hits,5}  {text}");

    int single = counts.Count(c => c.Hits == 1);

    Console.WriteLine();
    Console.WriteLine($"  кусков, дающих РОВНО один адрес: {single} из {counts.Count} ({100.0 * single / Math.Max(1, counts.Count):F0}%)");
}

async Task Dictionary()
{
    // Уровень = сколько развязок нужно, чтобы назвать строку. Ноль особый: финитного адреса нет,
    // строка достаётся якорем плюс Развязкой-3.
    int l2 = 0, l3 = 0, infinite = 0;

    foreach (var (_, fragment) in fragments)
        switch (fragment.Level)
        {
            case 1: l2++; break;
            case 2: l3++; break;
            default: infinite++; break;
        }

    Console.WriteLine();
    Console.WriteLine($"=== словарь: {fragments.Count} строк (L2 {l2}, L3 {l3}, infinite {infinite}) ===");

    foreach (var (id, fragment) in fragments.OrderBy(f => f.Key).Take(5))
        Console.WriteLine($"  {id,5}  {NameOf(fragment.Level),-8}  {fragment.Text}");

    if (fragments.Count > 5) Console.WriteLine($"  ... ещё {fragments.Count - 5}");
}

// Дерево цепочек - то, ради чего дамп и заводился.
//
// Печатаем иерархией, а не плоским списком: смысл дерева в общих ПРЕФИКСАХ, и в списке они не
// видны. Номер узла (он же номер прыжка) идёт первым - именно его клиент шлёт в /h/.
async Task Chains()
{
    var nodes = new List<(int Id, int? Parent, string Step, string? Url)>();

    await using (var cmd = db.CreateCommand(@"select ""Id"", ""ParentId"", ""Step"", ""Url"" from ""Chains"" order by ""Id"""))
    await using (var reader = await cmd.ExecuteReaderAsync())
        while (await reader.ReadAsync())
            nodes.Add((reader.GetInt32(0),
                       reader.IsDBNull(1) ? null : reader.GetInt32(1),
                       reader.GetString(2),
                       reader.IsDBNull(3) ? null : reader.GetString(3)));

    int leaves = nodes.Count(n => n.Url is not null);

    Console.WriteLine();
    Console.WriteLine($"=== дерево цепочек: {nodes.Count} узлов, {leaves} адресов ===");

    if (nodes.Count == 0)
    {
        Console.WriteLine("  пусто - в dev это норма: цепочки копятся в ОЗУ сервера (hypers=off),");
        Console.WriteLine("  а в базе должен лежать только посев из миграции DevChainSeed");

        return;
    }

    var children = nodes.ToLookup(n => n.Parent);

    Walk(null, 0);

    void Walk(int? parent, int depth)
    {
        foreach (var node in children[parent].OrderBy(n => n.Id))
        {
            string url = node.Url is null ? "" : "   -> " + node.Url;

            Console.WriteLine($"  {node.Id,4}  {new string(' ', depth * 2)}{StepOf(node.Step)}{url}");

            Walk(node.Id, depth + 1);
        }
    }
}

async Task Hypers()
{
    var hypers = new List<(int Handle, string Url)>();

    await using (var cmd = db.CreateCommand(@"select ""Id"", ""Url"" from ""Hypers"" order by ""Id"""))
    await using (var reader = await cmd.ExecuteReaderAsync())
        while (await reader.ReadAsync())
            hypers.Add((reader.GetInt32(0), reader.GetString(1)));

    Console.WriteLine();
    Console.WriteLine($"=== хайперы (старая плоская таблица): {hypers.Count} ===");

    foreach (var (handle, url) in hypers.Take(10)) Console.WriteLine($"  {handle,5}  {url}");

    if (hypers.Count > 10) Console.WriteLine($"  ... ещё {hypers.Count - 10}");
}

// Шаг цепочки: "f<адрес>" - фрагмент словаря, "r<руны>" - кусок, уехавший буквами.
string StepOf(string step)
{
    if (step.Length > 1 && step[0] == 'f' && int.TryParse(step[1..], out int id))
        return fragments.TryGetValue(id, out var fragment)
            ? $"{step,-6} {fragment.Text}"
            : $"{step,-6} (нет в словаре)";

    return $"{step,-6} (руны)";
}

static string NameOf(int level) => level switch { 1 => "L2", 2 => "L3", _ => "infinite" };
