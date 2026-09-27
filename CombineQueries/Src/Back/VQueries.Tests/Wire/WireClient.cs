using System.Text.Json.Nodes;

namespace CombineQueries.Tests.Wire;

// Порт пути Require/Result из Udon-клиента (Src/Front/Core/CombineQueries.cs): тот же выбор дороги -
// прыжок диапазоном, голова, сборка фрагментами и рунами, хвост, добор долга - и те же печёные
// адреса. Пауза между загрузками как у VRCStringDownloader: без неё долги приезжают не в те ответы,
// что в мире, и тест меряет другую систему.
public class WireClient(HttpClient http, string token, TimeSpan cooldown, bool hypers)
{
    private const string AlphabetEncoded = "abcdefghijklmnopqrstuvwxyz0123456789-._~%3A%2F%3F%23%5B%5D%40%21%24%26%27%28%29%2A%2B%2C%3B%3D%25";
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789-._~:/?#[]@!$&'()*+,;=%";
    private const string RuneAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789-._~:@!$&'()*+,;=";
    private const string Digits = "0123456789";
    private const string Scheme = "https";

    private const int Symbols = 59 + 35;
    private const int RuneSize = 3;
    private const int RuneWidth = 4;
    private const int NumSize = 4;
    // Размеры VF-пула. Синтетика Infinite подключается с маленькими (CQ_DFA x CQ_PAGES), чтобы дойти
    // до Inf за сотню строк, а не за 65 536, - как dev-режим сервера.
    private static readonly int DfaSize = int.Parse(Environment.GetEnvironmentVariable("CQ_DFA") ?? "1024");
    private static readonly int PageCount = int.Parse(Environment.GetEnvironmentVariable("CQ_PAGES") ?? "64");
    private const int HopCount = 64;
    private const int MaxChunks = 256;
    private const int MaxJumps = 4096;
    private const int HeadLimit = 2048;
    private const int HeadBases = 8;
    private const int RangeMax = 4;
    private const int SingleLimit = 1024;
    private const int PairLimit = 2048;
    private const int Pair2Limit = 64;

    // Виды кусков очереди - те же номера, что queueKind в клиенте.
    private const int Chunk = 0;
    private const int Fragment = 1;
    private const int Tail = 2;
    private const int Hop = 3;
    private const int SingleFragment = 8;
    private const int Pair = 9;
    private const int Pair2 = 10;

    private readonly List<string> _roots = [];
    private readonly SortedDictionary<int, string> _fragments = [];

    // Адрес Infinite по дереву: финитный предок и номер строки среди его Inf-потомков.
    private readonly Dictionary<int, (int Base, int Hop)> _infinite = [];
    private readonly Dictionary<string, int> _jumps = [];
    private readonly Dictionary<string, string> _bodies = [];
    private readonly Dictionary<string, int> _spent = [];

    private string _signs = "";
    private int _signPosition;
    private int _pending;
    private DateTime _lastLoad = DateTime.MinValue;

    private string[] _batch = [];
    private bool[] _done = [];
    private int _releasedAt;
    private bool _headTried;

    public int TotalQueries { get; private set; }

    public int Fragments => _fragments.Count;

    public int Infinite => _infinite.Count;

    // Сколько раз Inf уехал предком и номером под ним.
    public int HopsSent { get; private set; }

    public int Jumps => _jumps.Count;

    public string BodyOf(string url) => _bodies.GetValueOrDefault(PayloadOf(url), "");

    // Номер запроса, на котором приехало тело: 0 - сам vrequest, дальше - запросы после него. Как слот 2 коробки у клиента.
    public int SpentOf(string url) => _spent.GetValueOrDefault(PayloadOf(url));

    public async Task ConnectAsync()
    {
        var answer = await LoadAsync($"/connect?alphabet={AlphabetEncoded}&baseQuery=vink0s.com&runeSize={RuneSize}&scheme={Scheme}&token={token}"
            + $"&dfaSize={DfaSize}&pageCount={PageCount}&hopCount={HopCount}&rememberInfinite=true&resetHypers=false&hypers={(hypers ? "on" : "off")}");

        _roots.Clear();

        foreach (var root in Items(answer, "roots")) _roots.Add(root!.GetValue<string>());

        _signs = answer["signs"]?.GetValue<string>() ?? "";
        _signPosition = 0;

        foreach (var jump in Items(answer, "jumps")) _jumps[PayloadOf(Text(jump, "url"))] = Number(jump, "jump");

        Learn(answer);
    }

    // Result: выпускает пачку и держит её, пока не приедут все тела. step зовётся после каждого
    // запроса - это кадр мира, в котором риг смотрит в коробки.
    public async Task RunAsync(IReadOnlyList<string> urls, Action step)
    {
        foreach (string url in urls)
        {
            _bodies.Remove(PayloadOf(url));
            _spent.Remove(PayloadOf(url));
        }

        _batch = [.. urls];
        _done = new bool[urls.Count];
        _releasedAt = TotalQueries;

        while (true)
        {
            int start = -1;

            for (int i = 0; i < _batch.Length; i++)
                if (!_done[i] && _jumps.TryGetValue(PayloadOf(_batch[i]), out int first) && (start < 0 || first < start)) start = first;

            if (start >= 0) { await RangeAsync(start); step(); continue; }

            int next = Array.IndexOf(_done, false);

            if (next < 0) break;

            await DispatchAsync(_batch[next]);
            step();
        }

        // Долги: за этими телами сервер ещё ходит наружу. Клиент добирает их, пока сервер говорит
        // pending; молчащий сервер - три пустых добора, дальше тело уже не приедет.
        for (int idle = 0; urls.Any(url => BodyOf(url) == "") && idle < 3;)
        {
            if (_pending == 0) idle++;

            Debt(await LoadAsync($"/tc/{NextSign()}"));
            step();
        }
    }

    private async Task RangeAsync(int first)
    {
        var answer = await LoadAsync($"/h/{Num(first)}/{Num(RangeMax)}/{NextSign()}");

        int marked = TakeSent(answer);

        Debt(answer);

        // Прыжок не узнан и никого не назвал - клиент крутился бы на нём вечно. Забываем номер,
        // адрес уйдёт сборкой.
        if (marked == 0)
            foreach (string url in _batch)
                if (_jumps.GetValueOrDefault(PayloadOf(url), -1) == first) _jumps.Remove(PayloadOf(url));
    }

    private async Task DispatchAsync(string url)
    {
        string payload = PayloadOf(url);

        _headTried = false;

        await CombineAsync(payload, payload);

        Mark(payload);
    }

    private async Task CombineAsync(string pending, string payload)
    {
        var values = new List<int>();
        var kinds = new List<int>();

        int accumulated = 0, accumulatedLength = 0, position = 0;

        while (position < payload.Length)
        {
            if (accumulatedLength == 0)
            {
                var (id, length) = FragmentAt(payload, position);

                // Infinite едет предком (обычный VF) и номером под ним (hop).
                var (anchor, hop) = _infinite.GetValueOrDefault(id, (id, 0));
                int cost = hop > 0 ? 2 : 1;

                if (id >= 0 && cost <= (length + RuneSize - 1) / RuneSize && values.Count + cost <= MaxChunks)
                {
                    values.Add(anchor);
                    kinds.Add(Fragment);

                    if (hop > 0) { values.Add(hop); kinds.Add(Hop); }

                    position += length;
                    continue;
                }
            }

            var (symbol, symbolLength) = SymbolAt(payload, position);

            if (symbol < 0) throw new InvalidOperationException("character outside the alphabet: " + payload);

            accumulated = accumulated * Symbols + symbol;
            accumulatedLength++;
            position += symbolLength;

            // Первая руна адреса: «:» последним символом - «будет ещё». Без метки она уходит, только
            // если адрес ею и кончается; настоящий «:» на этом месте едет первым в следующем куске.
            if (values.Count == 0 && accumulatedLength == RuneSize - 1 && position < payload.Length)
            {
                var (next, nextLength) = SymbolAt(payload, position);

                if (next == Alphabet.IndexOf(':') || position + nextLength < payload.Length)
                {
                    // «:» перед меткой дал бы полосу из двух, а полоса - это одиночка.
                    if (accumulated % Symbols == Alphabet.IndexOf(':')) throw new InvalidOperationException("':' as the second symbol reads as a whole address: " + payload);

                    values.Add(accumulated * Symbols + Alphabet.IndexOf(':'));
                    kinds.Add(Chunk);

                    accumulated = 0;
                    accumulatedLength = 0;

                    continue;
                }
            }

            if (accumulatedLength < RuneSize) continue;

            values.Add(accumulated);
            kinds.Add(Chunk);

            accumulated = 0;
            accumulatedLength = 0;
        }

        int tail = accumulatedLength == 0 ? 0 : accumulatedLength == 1 ? 1 + accumulated : 1 + Symbols + accumulated;

        // Одна руна без метки - весь адрес: закрывает сама, хвост не нужен.
        bool single = tail == 0 && values.Count == 1 && kinds[0] == Chunk;

        // Фрагмент и одна буква - тоже весь адрес в первой /c: одна ссылка, хвост не нужен.
        bool pair = values.Count == 1 && kinds[0] == Fragment && accumulatedLength == 1 && values[0] < PairLimit
            && accumulated < Alphabet.Length && accumulated != Alphabet.IndexOf(':');

        // Фрагмент и две буквы - так же, для первых фрагментов.
        bool pair2 = values.Count == 1 && kinds[0] == Fragment && accumulatedLength == 2 && values[0] < Pair2Limit
            && accumulated / Symbols < Alphabet.Length && accumulated % Symbols < Alphabet.Length && accumulated % Symbols != Alphabet.IndexOf(':');

        if (pair) { values[0] = values[0] * Alphabet.Length + accumulated; kinds[0] = Pair; }
        else if (pair2) { values[0] = values[0] * Symbols * Symbols + accumulated; kinds[0] = Pair2; }
        else if (tail == 0 && values.Count > 0 && kinds[^1] == Fragment && values[^1] < SingleLimit) kinds[^1] = SingleFragment;
        else if (!single) { values.Add(tail); kinds.Add(Tail); }

        int jump = _jumps.GetValueOrDefault(payload, -1);

        if (jump < 0 && !_headTried && await HeadAsync(payload)) return;

        if (jump >= 0 && jump < MaxJumps)
        {
            var hyper = await LoadAsync($"/h/{Num(jump)}/{Num(RangeMax)}/{NextSign()}");

            TakeSent(hyper);
            Debt(hyper);

            if (hyper["known"]?.GetValue<bool>() == true) return;

            _jumps.Remove(pending);

            await CombineAsync(pending, pending);
            return;
        }

        JsonNode answer = new JsonObject();

        for (int i = 0; i < values.Count; i++)
        {
            if (kinds[i] == Hop) HopsSent++;

            answer = await LoadAsync(kinds[i] switch
            {
                Chunk => $"/c/{Runes(values[i], RuneAlphabet, RuneWidth)}/0/0/0/0",
                Fragment => $"/c/{Runes(0, RuneAlphabet, RuneWidth)}/{values[i] % DfaSize}/{values[i] / DfaSize}/0/1/{NextSign()}",
                Hop => $"/c/{Runes(0, RuneAlphabet, RuneWidth)}/0/0/{values[i]}/1/{NextSign()}",
                // Флаг склейки: 1 - закрывает цепочку, 0 - адрес весь в этом запросе.
                SingleFragment => $"/sf/{Num(values[i])}/{(i > 0 ? 1 : 0)}/{NextSign()}",
                Pair => $"/c/{PairRune(values[i] % Alphabet.Length)}/{values[i] / Alphabet.Length % DfaSize}/{values[i] / Alphabet.Length / DfaSize}/0/2",
                Pair2 => $"/c/{Runes(values[i] % (Symbols * Symbols) * Symbols + Alphabet.IndexOf(':'), RuneAlphabet, RuneWidth)}/{values[i] / (Symbols * Symbols) % DfaSize}/{values[i] / (Symbols * Symbols) / DfaSize}/0/2",
                _ => $"/t/{TailRunes(values[i])}/{(i > 0 ? 1 : 0)}/{NextSign()}"
            });

            Debt(answer);
        }

        int leaf = answer["leaf"]?.GetValue<int>() ?? -1;

        if (leaf >= 0) _jumps[pending] = leaf;

        Learn(answer);
    }

    private async Task<bool> HeadAsync(string payload)
    {
        int cut = payload.LastIndexOf('/');

        if (cut < 0 || cut + 1 >= payload.Length) return false;

        string differs = payload[(cut + 1)..];
        string common = payload[..(cut + 1)];

        int piece = _fragments.Where(pair => pair.Value == differs && pair.Key < HeadLimit).Select(pair => pair.Key).DefaultIfEmpty(-1).First();

        if (piece < 0) return false;

        int found = _jumps.Where(pair => pair.Key.Length > cut && pair.Key.StartsWith(common, StringComparison.Ordinal)).Select(pair => pair.Value).DefaultIfEmpty(-1).First();

        if (found < 0) return false;

        _headTried = true;

        // Третье число - флаг полноты: кусок равен всему расхождению, значит адрес головы полный.
        var answer = await LoadAsync($"/hd/{Num(piece)}/{Num(found % HeadBases)}/1/{NextSign()}");

        bool ours = false;

        foreach (var item in Items(answer, "found"))
        {
            string url = PayloadOf(Text(item, "url"));

            _jumps[url] = Number(item, "jump");

            if (url == payload) ours = true;
        }

        Debt(answer);

        if (BodyOf(payload) != "") return true;

        string kept = answer["kept"]?.GetValue<string>() ?? "";

        await CombineAsync(payload, ours || kept == "" ? payload : payload[..^kept.Length]);

        return true;
    }

    // Прыжок назвал адреса: каждый оседает в памяти номеров и снимается с пачки. Возвращает сколько.
    private int TakeSent(JsonNode answer)
    {
        int marked = 0;

        foreach (var item in Items(answer, "sent"))
        {
            string url = PayloadOf(Text(item, "url"));

            _jumps[url] = Number(item, "jump");

            if (Mark(url)) marked++;
        }

        return marked;
    }

    private void Debt(JsonNode answer)
    {
        // Новое кольцо частей: старое кончилось на этом запросе, дальше идём по свежему с начала.
        string fresh = answer["signs"]?.GetValue<string>() ?? "";

        if (fresh != "" && fresh != _signs) { _signs = fresh; _signPosition = 0; }

        _pending = Math.Max(0, answer["pending"]?.GetValue<int>() ?? 0);

        foreach (var item in Items(answer, "ready"))
        {
            string url = PayloadOf(Text(item, "url"));

            if (url == "") continue;

            _bodies[url] = Text(item, "response");

            if (!_spent.ContainsKey(url) && _batch.Any(asked => PayloadOf(asked) == url)) _spent[url] = Math.Max(0, TotalQueries - _releasedAt - 1);

            Mark(url);
        }
    }

    private bool Mark(string payload)
    {
        for (int i = 0; i < _batch.Length; i++)
            if (!_done[i] && PayloadOf(_batch[i]) == payload) { _done[i] = true; return true; }

        return false;
    }

    private void Learn(JsonNode answer)
    {
        foreach (var item in Items(answer, "fragments"))
        {
            int id = Number(item, "id");
            string text = Text(item, "text");

            if (id < 0 || text == "") continue;

            _fragments.TryAdd(id, text);

            int hop = Number(item, "hop");

            if (hop > 0) _infinite[id] = (Number(item, "base"), hop);
        }
    }

    private (int Id, int Length) FragmentAt(string payload, int position)
    {
        int id = -1, length = 0;

        int capacity = DfaSize * PageCount;

        foreach (var (key, text) in _fragments)
        {
            // Первая сверка, на фронте: Inf - только с адресом по дереву, влезающим в пулы.
            bool addressed = _infinite.TryGetValue(key, out var at) ? at.Base >= 0 && at.Base < capacity && at.Hop < HopCount : key < capacity;

            if (addressed && text.Length > length && payload.AsSpan(position).StartsWith(text, StringComparison.Ordinal)) { id = key; length = text.Length; }
        }

        return (id, length);
    }

    private (int Symbol, int Length) SymbolAt(string payload, int position)
    {
        int best = -1, length = 0;

        for (int i = 0; i < _roots.Count; i++)
            if (_roots[i].Length > length && payload.AsSpan(position).StartsWith(_roots[i], StringComparison.Ordinal)) { best = i; length = _roots[i].Length; }

        return best >= 0 ? (Alphabet.Length + best, length) : (Alphabet.IndexOf(payload[position]), 1);
    }

    private int NextSign()
    {
        if (_signs.Length == 0) return 0;

        int sign = _signs[_signPosition] - '0';

        _signPosition = (_signPosition + 1) % _signs.Length;

        return sign;
    }

    private async Task<JsonNode> LoadAsync(string path)
    {
        var wait = _lastLoad + cooldown - DateTime.UtcNow;

        if (wait > TimeSpan.Zero) await Task.Delay(wait);

        _lastLoad = DateTime.UtcNow;
        TotalQueries++;

        using var response = await SendAsync(path);

        string text = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{path} -> {(int)response.StatusCode}: {text}");

        return (text.StartsWith('{') ? JsonNode.Parse(text) : null) ?? new JsonObject();
    }

    // Один повтор на обрыв связи. Между загрузками мы держим паузу SDK, и за неё keep-alive успевает
    // умереть - на хосте это видно как запрос, висящий до таймаута на уже мёртвом сокете. Мир на
    // такое отвечает сторожем рига, тест - повтором: диагностировать протокол по оборванному
    // соединению всё равно нечем.
    private async Task<HttpResponseMessage> SendAsync(string path)
    {
        try
        {
            return await http.GetAsync(path);
        }
        catch (Exception broken) when (broken is HttpRequestException or TaskCanceledException or IOException)
        {
            return await http.GetAsync(path);
        }
    }

    private static string TailRunes(int value)
    {
        int pad = Alphabet.IndexOf(':');

        int first = value == 0 ? pad : value <= Symbols ? value - 1 : (value - 1 - Symbols) / Symbols;
        int second = value > Symbols ? (value - 1 - Symbols) % Symbols : pad;

        int runes = first * Symbols + second;

        for (int i = 2; i < RuneSize; i++) runes = runes * Symbols + pad;

        return Runes(runes, RuneAlphabet, RuneWidth);
    }

    private static string PairRune(int letter) => Runes((letter * Symbols + Alphabet.IndexOf(':')) * Symbols + Alphabet.IndexOf(':'), RuneAlphabet, RuneWidth);

    private static string Num(int value) => Runes(value, Digits, NumSize);

    private static string Runes(int value, string alphabet, int width)
    {
        var runes = new char[width];

        for (int i = width - 1; i >= 0; i--)
        {
            runes[i] = alphabet[value % alphabet.Length];
            value /= alphabet.Length;
        }

        return new string(runes);
    }

    private static string PayloadOf(string url) => url.StartsWith(Scheme + "://", StringComparison.Ordinal) ? url[(Scheme.Length + 3)..] : url;

    private static IEnumerable<JsonNode?> Items(JsonNode answer, string field) => answer[field] as JsonArray ?? [];

    private static string Text(JsonNode? item, string field) => item?[field]?.GetValue<string>() ?? "";

    private static int Number(JsonNode? item, string field) => item?[field]?.GetValue<int>() ?? -1;
}
