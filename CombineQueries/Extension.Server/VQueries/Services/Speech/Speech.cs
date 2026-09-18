using System.Diagnostics;
using System.Text;
using CombineQueries.Domain.Aggregates.Translator;
using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Services.Speech;

public class Speech : ISpeech
{
    public string? Alphabet { get; private set; }
    public string? RuneAlphabet { get; private set; }
    public int RuneSize { get; private set; } = 3;
    public string Scheme { get; private set; } = "https";
    public int DfaSize { get; private set; }
    public int PageCount { get; private set; } = 1;
    public int HopCount { get; private set; } = 1;

    // Кладём ли НОВЫЕ цепочки в персист. Решает клиент параметром hypers в connect.
    public bool Hypers { get; private set; } = true;

    // Контекст подключения, за которым обработчик события идёт вместо аргументов.
    public string BaseForwardUrl { get; private set; } = "";

    public bool ResetHypers { get; private set; }
    public string DirectRunes { get; set; } = string.Empty;
    public string DirectUnruned { get; set; } = string.Empty;
    public bool Authorized { get; private set; }

    // Поток сборки - упорядоченный: между рун-кусками (чанк) вклиниваются виртуальные фрагменты (VF).
    // Кусок либо руна (декод в Close по типу хвоста), либо VF (готовый текст по id). Запросы Udon
    // последовательны, порядок прихода = порядок в URL.
    private readonly List<Piece> _pieces = [];

    // Хайпер-дерево: цепочки запросов по слоям. Пока без персиста - копим и меряем.
    private readonly HyperTree _tree = new();

    public int TreeChains => _tree.Chains;
    public int TreeNodes => _tree.Nodes;
    public int TreeDeepest => _tree.Deepest;

    // Сколько адресов ещё возможно после уже принятого префикса. 1 значит, что продолжение
    // однозначно - на этом строится будущий досрочный форвард.
    public int Ahead() => _tree.Ahead(StepsOf());

    // Персист дерева: что появилось с прошлого раза и как поднять сохранённое.
    public IReadOnlyList<(int Id, int? ParentId, string Step, string? Url)> TakeChains() => _tree.TakePending();

    // Дерево поднимается через Forget, то есть прогрев после него стёрт - снимаем отметку, чтобы
    // connect нагрел заново. Иначе прогретое жило бы ровно до следующего подключения.
    public void RestoreChains(IEnumerable<(int Id, int? ParentId, string Step, string? Url)> nodes)
    {
        _tree.Restore(nodes);

        _preheated = false;
    }

    // Сид хайпера для клиента: адрес -> номер прыжка. Только листы, промежуточные узлы фронту
    // не нужны - он держит плоский словарь, а не дерево.
    public IEnumerable<(string Url, int Jump)> ChainLeaves() => _tree.Leaves();

    // Сложение базы и куска: гипер даёт начало адреса, кусок - расхождение.
    //
    // Клиент знает похожий адрес, но не знает номера нужного. Он присылает кусок и ОБРЕЗОК номера
    // похожей цепочки; сервер достраивает адрес и отвечает точно, а не списком похожих.
    //
    // Порядок важен: сперва смотрим, знаком ли вообще кусок. Не знаком - базы не трогаем вовсе,
    // складывать не с чем.
    public IEnumerable<(string Url, int Jump)> ChainsFrom(int shortened, string text, int limit)
    {
        var known = new Dictionary<string, int>();

        foreach (var (url, jump) in _tree.Leaves()) known[url] = jump;

        bool anywhere = false;

        foreach (string url in known.Keys)
            if (url.Contains(text, StringComparison.Ordinal)) { anywhere = true; break; }

        if (!anywhere) yield break;

        int found = 0;

        // Разные базы достраивают один и тот же адрес - отдавать его дважды значит форвардить
        // дважды, то есть платить чужим API за собственную арифметику.
        var given = new HashSet<string>();

        foreach (var (url, jump) in known)
        {
            if (found >= limit) yield break;

            // Обрезок сужает круг баз: полный номер в адрес запроса не влезает, а младших битов
            // хватает - дальше отсеивает сам кусок.
            if ((jump & HeadBaseMask) != shortened) continue;

            int cut = url.LastIndexOf('/');

            if (cut < 0) continue;

            string guess = url[..(cut + 1)] + text;

            if (!known.TryGetValue(guess, out int number)) continue;
            if (!given.Add(guess)) continue;

            found++;

            yield return (guess, number);
        }
    }

    // Сколько младших битов номера едет в голове. Столько же берёт клиент (HeadBases = 8).
    //
    // Три бита, а не четыре: разрядность в печёных ссылках делится между куском и базой, и куску
    // она нужнее. База - обрезок, промах по ней стоит лишь пары названных соседей, а те клиенту
    // достаются даром; кусок за потолком не называется вовсе.
    public const int HeadBaseMask = 7;

    // Номера последней собранной цепочки: лист (весь url) и последний общий узел с известными.
    public int LastLeaf { get; private set; } = -1;
    public int LastPrefix { get; private set; } = -1;
    public int LastShared { get; private set; }

    // Встать в середину цепочки по номеру узла: восстанавливаем куски пути, дальше поток идёт
    // обычными /c/. Это и есть новый смысл хайпера - прыжок не на адрес, а в точку цепочки.
    public int Resume(int handle)
    {
        if (Alphabet is null || RuneAlphabet is null) throw new Exception("CRIT: /connect was not called");

        var path = _tree.PathOf(handle);

        if (path is null) return -1;

        _pieces.Clear();
        _pendingPage = 0;
        _assembly.Restart();

        foreach (string step in path)
        {
            if (step.Length == 0) continue;

            // Шаг закодирован как "f<адрес>" либо "r<текст>" - разбираем обратно в кусок сборки.
            // Рунный шаг хранится разжатым, поэтому кладём его готовым текстом, а не декодируем.
            // "t<текст>" - формат старых записей, до канонизации; читаем как текст, чтобы поднятое
            // из персиста дерево не рассыпалось.
            if (step[0] == 'f' && int.TryParse(step[1..], out int id)) _pieces.Add(new Piece(true, "", id));
            else _pieces.Add(new Piece(false, "", 0, step[1..]));
        }

        return _pieces.Count;
    }

    // Кусок промахнувшейся головы: она его уже получила, значит запрос за ним оплачен. Держим до
    // закрытия и приклеиваем концом адреса - клиенту остаётся досказать только начало.
    private string _trailing = "";

    public void Keep(string text) => _trailing = text;

    public string? UrlOf(int handle) => _tree.UrlOf(handle);

    public IEnumerable<(string Url, int Jump)> Family(int handle, int limit) => _tree.Family(handle, limit);

    private List<string> StepsOf()
    {
        var steps = new List<string>(_pieces.Count);

        foreach (var piece in _pieces) steps.Add(HyperTree.StepOf(piece.IsFragment, piece.Rune, piece.FragmentId));

        return steps;
    }

    private readonly List<string> _handles = [];
    private readonly List<long> _firstSendMs = [];
    private readonly Dictionary<string, int> _byUrl = [];

    // VF-словарь. _phrases - весь LZW-словарь фраз (для роста), _fragments - адресная таблица id->text.
    // id это ГЛОБАЛЬНЫЙ адрес: [0, DfaSize) = L2 (одна развязка /f/), [DfaSize, DfaSize*PageCount) = L3
    // (две развязки /g/+/f/, id = page*DfaSize + offset). L1 (корни) едут в руне, тут их нет.
    private readonly HashSet<string> _phrases = [];
    private readonly List<string> _fragments = [];
    private readonly Dictionary<string, int> _fragIndex = [];

    // Регистр страницы L3: /g/<page> ставит его, следующий /f/<offset> берёт id = page*DfaSize+offset
    // и сбрасывает в 0 (без /g/ это L2: id = offset).
    private int _pendingPage;

    private const int FragmentMinLength = 6;

    // Коды received для CombineNew: чанк -> число кусков (>0); VF -> уровень.
    public const int VFL2 = -4;
    public const int VFL3 = -5;
    public const int VFInfinite = -6;

    // Хоп не разрешился: цепь оборвалась (звена нет). Клиент дошлёт строку буквами.
    public const int VFBroken = -7;

    private readonly Stopwatch _assembly = new();
    private readonly StringBuilder sb = new();
    private readonly StringBuilder direct = new();
    private readonly StringBuilder unruned = new();

    private string _authBuffer = "";

    private const int AuthMax = 128;

    // Text - готовая строка: так приходят куски, восстановленные из дерева, где шаг руны хранится
    // разжатым. У живых кусков от клиента его нет, они декодируются из Rune как раньше.
    private readonly record struct Piece(bool IsFragment, string Rune, int FragmentId, string? Text = null);

    public bool Broken { get; private set; }

    // Подписи хвоста - ГОТОВО, НО НЕ ВКЛЮЧЕНО (обновление релиза): последовательность рождается,
    // но наружу не уезжает и никто её не сверяет. Включение: вернуть поле в ConnectResponse,
    // сегмент {sign} в роут /t/, сверку в TailHandler и множитель в клиентский TailPool.
    // Сервер рождает последовательность на connect и отдаёт её клиенту; дальше
    // КАЖДЫЙ хвост обязан назвать очередную. Не совпало - запрос чужой или поток разъехался,
    // и приём валится до повторного connect (одна попытка, перебор бессмыслен).
    //
    // Живёт в хвосте, а не в чанках: у /t/ свой маленький пул (8931), и подпись множит только его.
    // В /c/ то же самое стоило бы 830 584 * SignValues.
    public string Signs { get; private set; } = "";

    // Поток одного клиента: его кольцо, позиция в нём и когда по нему в последний раз приходили.
    // Клиентов много, поэтому и потоков много: сервер держит по одному на каждое подключение.
    private sealed class SignStream
    {
        public string Ring = "";
        public int Position;
        public DateTime Seen;

        public int Next => Ring[Position] - '0';
    }

    private readonly List<SignStream> _streams = [];

    // Список правят разные запросы сразу: поиск, сдвиг и выдача нового кольца обязаны идти целиком,
    // иначе двое разберут один поток пополам.
    private readonly object _signLock = new();

    // Сколько подключений помним. Больше - вытесняем самое давнее: его клиент либо ушёл, либо
    // подключится заново, а бесконечный список искал бы чужую часть тем дольше, чем дольше живёт сервер.
    private const int MaxStreams = 32;

    public const int SignValues = 8;

    private const int SignLength = 256;

    // Сверяет очередную подпись и продвигает позицию. Последовательность идёт по кругу: длины
    // хватает, чтобы наблюдение за одним URL не выдавало следующую.
    //
    // Кольцо ОДНО на /t/ и /h/: каждый из них съедает по позиции, поэтому расходятся они сразу же,
    // если кто-то влез в поток. Разница только в разрешении: хвост сверяет подпись целиком
    // (SignValues значений), прыжок - её младший бит.
    //
    // Почему у прыжка бит: печётся КАЖДОЕ сочетание, поэтому полная подпись умножила бы пул
    // прыжков на SignValues (4096 -> 32768 ссылок), а бит удваивает (4096 -> 8192).
    public bool CheckSign(int sign) => CheckSign(sign, SignValues);

    // Пришедшую часть ищем среди ОЖИДАЕМЫХ - по одной на подключённого клиента. Нашлась - поток
    // известный, сдвигаем его на следующую; не нашлась - часть чужая, и запрос отбивается.
    //
    // Части всего SignValues, поэтому двое могут ждать одну и ту же. Тогда берём поток, по которому
    // приходили позже: клиент шлёт запросы очередью, и свежий поток куда вероятнее.
    public bool CheckSign(int sign, int values)
    {
        if (sign < 0 || sign >= values) return false;

        lock (_signLock)
        {
            if (_streams.Count == 0) return true;

            SignStream? mine = null;

            foreach (var stream in _streams)
                if (stream.Next % values == sign && (mine is null || stream.Seen > mine.Seen)) mine = stream;

            if (mine is null) return false;

            mine.Position++;
            mine.Seen = DateTime.UtcNow;

            // Кольцо кончилось - на его место рождается новое, и уезжает тем же ответом, в котором
            // клиент потратил последнюю часть. Тот, кто подслушал кольцо целиком, получает его мёртвым:
            // следующая часть придёт уже из того, которого он не видел.
            if (mine.Position < mine.Ring.Length) return true;

            mine.Ring = NewSigns();
            mine.Position = 0;

            _fresh.Value = mine.Ring;

            return true;
        }
    }

    // Новый поток на connect: кольцо рождается здесь и уезжает клиенту, сервер оставляет себе
    // позицию в нём. Своё кольцо у каждого - по нему клиенты и различаются.
    private string OpenSigns()
    {
        string ring = NewSigns();

        lock (_signLock)
        {
            if (_streams.Count >= MaxStreams)
            {
                var oldest = _streams[0];

                foreach (var stream in _streams) if (stream.Seen < oldest.Seen) oldest = stream;

                _streams.Remove(oldest);
            }

            _streams.Add(new SignStream { Ring = ring, Position = 0, Seen = DateTime.UtcNow });
        }

        return ring;
    }

    public int Streams { get { lock (_signLock) return _streams.Count; } }

    // Привязано к ЗАПРОСУ, а не к серверу: сервер один на всех, и общее поле отдало бы новое кольцо
    // тому, чей ответ собрался первым. AsyncLocal живёт внутри той же цепочки вызовов, что и проверка.
    private static readonly AsyncLocal<string> _fresh = new();

    // Кольцо, выданное взамен кончившегося. Забирает его ОДИН ответ - тот, что его и увезёт клиенту.
    public string TakeFreshSigns()
    {
        string fresh = _fresh.Value ?? "";

        _fresh.Value = "";

        return fresh;
    }

    // Сколько значений у подписи прыжка: один бит.
    public const int JumpSignValues = 2;

    private static string NewSigns()
    {
        var signs = new char[SignLength];

        for (int i = 0; i < SignLength; i++) signs[i] = (char)('0' + System.Security.Cryptography.RandomNumberGenerator.GetInt32(SignValues));

        return new string(signs);
    }

    // Новый мастер - новый прогрев: снимаем отметку, чтобы ближайший connect нагрел заново.
    public void Authorize()
    {
        Authorized = true;
        _preheated = false;
    }

    private bool _preheated;

    // Прогрев хайперов по текущему словарю: каждая строка, которая сама по себе адрес, получает
    // готовую цепочку, и клиент берёт её одним /h/ ещё до того, как хоть раз её собирал.
    //
    // Зовётся на connect, ПОСЛЕ восстановления дерева из персиста: тот поднимается через Forget и
    // затёр бы прогрев, сделай мы его раньше (а авторизация мастера идёт как раз до connect).
    // Отметку снимает Authorize, поэтому греем один раз на мастера. Пустой словарь греть нечем.
    //
    // Берём НЕ все строки: словарь состоит из подстрок, а греть можно только те, что сами по себе
    // складываются в цельный запрос по url - urlRequests. Префикс вроде "site.com/carts/" таким не
    // является, и прыжок по нему увёл бы наружу заведомо кривой адрес.
    public int Preheat()
    {
        if (_preheated || _fragments.Count == 0) return 0;

        _preheated = true;

        int warmed = 0;

        for (int id = 0; id < _fragments.Count; id++)
        {
            string text = _fragments[id];

            if (!Jumpable(text)) continue;

            // Путь считаем тем же разбором, что и на записи: прогретая цепочка обязана совпасть
            // с той, которую построит живой проход, иначе адрес заведётся дважды.
            _tree.Remember(Canonical(text), text);

            warmed++;
        }

        return warmed;
    }

    // Можно ли по строке заводить прыжок: она цельный запрос и не обрубок более длинного слова.
    //
    // Смотрит в словарь, поэтому звать ПОСЛЕ его заливки. Тем же отбором connect выпалывает из
    // персиста цепочки, которые прогрев успел туда записать, пока фильтр был мягче.
    public bool Jumpable(string url) => IsUrlRequest(url) && !IsStub(url);

    // urlRequest - строка словаря, которая сама по себе цельный запрос, а не обрубок.
    //
    // Отбор строгий намеренно: прогретый адрес попадает в дерево, а оттуда его начинает отдавать
    // голова - и каждый обрубок стоит холостого похода наружу за чужой счёт. Раньше сюда пролезали
    // ".com/comments" (хост начинается с точки), "…/search?q" (параметр без значения), голый
    // "site.com" (корень сайта отдаёт HTML-страницу, а не ответ API) и "…?limit=10&skip" (второй
    // параметр без значения - проверялся только первый).
    private static bool IsUrlRequest(string text)
    {
        if (text.Length < 5) return false;

        int slash = text.IndexOf('/');

        // Без пути это корень сайта. Базовый адрес - кусок словаря, но не адрес для прыжка.
        if (slash < 0) return false;

        string host = text[..slash];
        string rest = text[slash..];

        if (!Host(host)) return false;

        // Пустой сегмент пути - обрубок склейки, наружу с таким идти незачем.
        if (rest.Contains("//", StringComparison.Ordinal)) return false;

        int query = rest.IndexOf('?');

        // Есть вопрос - значит у КАЖДОГО параметра должно быть значение: "?limit" и "&skip" обрубки.
        if (query >= 0)
            foreach (string pair in rest[(query + 1)..].Split('&'))
            {
                int equals = pair.IndexOf('=');

                if (equals <= 0 || equals + 1 >= pair.Length) return false;
            }

        char last = text[^1];

        return last != '/' && last != '?' && last != '&' && last != '=' && last != '.' && last != '-';
    }

    // Обрубок: словарь знает то же слово длиннее. LZW растит фразу по символу, поэтому рядом с
    // "site.com/products" в нём лежат "site.com/p", "/pro", "/product", а рядом с хостом - "site.co",
    // и каждый такой снаружи это 404 или вовсе чужой домен.
    //
    // Слово продолжается буквой после буквы или связкой '-', '_'. Цифра после цифры обрубком не
    // считается: "products/1" и "products/12" - два разных адреса. Запросы с '?' здесь не судим:
    // обрубок параметра уже отсёк IsUrlRequest, а значение "q=Jo" рядом с "q=John" - такой же
    // настоящий запрос, как и длинный.
    private bool IsStub(string text)
    {
        if (text.Contains('?')) return false;

        var sorted = SortedFragments();

        int at = Array.BinarySearch(sorted, text, StringComparer.Ordinal);

        // Все продолжения строки лежат в сортировке подряд сразу за ней.
        for (int i = at < 0 ? ~at : at + 1; i < sorted.Length && sorted[i].StartsWith(text, StringComparison.Ordinal); i++)
        {
            if (sorted[i].Length == text.Length) continue;

            char next = sorted[i][text.Length];

            if (next == '-' || next == '_') return true;
            if (char.IsAsciiLetter(next) && char.IsAsciiLetterOrDigit(text[^1])) return true;
            if (char.IsAsciiDigit(next) && char.IsAsciiLetter(text[^1])) return true;
        }

        return false;
    }

    // Словарь по возрастанию текста - для поиска продолжений. Словарь только растёт, поэтому
    // устаревание видно по размеру; Restore подменяет его целиком и сбрасывает явно.
    private string[] SortedFragments()
    {
        if (_sorted.Length == _fragments.Count) return _sorted;

        _sorted = [.. _fragments];

        Array.Sort(_sorted, StringComparer.Ordinal);

        return _sorted;
    }

    private string[] _sorted = [];

    // Хост: непустое имя, точка внутри (не с краю) и буквенная зона длиной от двух символов.
    private static bool Host(string host)
    {
        int dot = host.LastIndexOf('.');

        if (dot <= 0 || dot + 1 >= host.Length) return false;
        if (host[0] == '.' || host[0] == '-') return false;

        string zone = host[(dot + 1)..];

        if (zone.Length < 2) return false;

        foreach (char c in zone) if (!char.IsAsciiLetter(c)) return false;

        return true;
    }

    public void Fault(string reason)
    {
        Broken = true;

        _pieces.Clear();
        _pendingPage = 0;

        LastFault = reason;
    }

    public string LastFault { get; private set; } = "";

    public void AuthAppend(string segment)
    {
        _authBuffer += segment;

        if (_authBuffer.Length > AuthMax) _authBuffer = _authBuffer[..AuthMax];
    }

    public string AuthConsume()
    {
        string current = _authBuffer;

        _authBuffer = "";

        return current;
    }

    public void SetContext(ISetContextCommand<char> command)
    {
        Alphabet = command.Alphabet;
        RuneAlphabet = Translator.RuneAlphabetOf(command.Alphabet);
        RuneSize = command.RuneSize;
        Scheme = command.Scheme;
        DfaSize = command.DfaSize;
        PageCount = command.PageCount < 1 ? 1 : command.PageCount;
        HopCount = command.HopCount < 1 ? 1 : command.HopCount;
        Hypers = command.Hypers;
        BaseForwardUrl = command.BaseForwardUrl;
        ResetHypers = command.ResetHypers;

        // Чистим только незавершённую сборку. Хайперы и фрагменты НЕ трогаем: при реконнекте
        // (повторный connect) они остаются тёплыми и уезжают сидом.
        _pieces.Clear();
        _pendingPage = 0;

        // Connect - единственный способ снять срыв приёма. Заодно рождается новая последовательность
        // подписей: старая после сбоя могла быть подсмотрена.
        Broken = false;
        LastFault = "";

        Signs = OpenSigns();
    }

    // Заливка тёплого словаря из персиста (вызывается на connect, после SetContext). Индекс списка
    // здесь И ЕСТЬ адрес/handle, поэтому дырки в нумерации добиваем пустыми - иначе всё поедет.
    // Сиды ждём упорядоченными по id/handle.
    public void Restore(IReadOnlyList<FragmentSeed> fragments, IReadOnlyList<HyperSeed> hypers)
    {
        _fragments.Clear();
        _fragIndex.Clear();
        _phrases.Clear();

        _sorted = [];

        foreach (var seed in fragments)
        {
            if (seed.Id < 0) continue;

            while (_fragments.Count <= seed.Id) _fragments.Add("");

            _fragments[seed.Id] = seed.Text;
            _fragIndex[seed.Text] = seed.Id;

            // Кладём и в LZW-словарь фраз: иначе обучение заново «откроет» уже известную строку.
            _phrases.Add(seed.Text);
        }

        _handles.Clear();
        _byUrl.Clear();
        _firstSendMs.Clear();

        foreach (var seed in hypers)
        {
            if (seed.Handle < 0) continue;

            while (_handles.Count <= seed.Handle) { _handles.Add(""); _firstSendMs.Add(-1); }

            _handles[seed.Handle] = seed.Url;
            _byUrl[seed.Url] = seed.Handle;
        }
    }

    // Обычный чанк-руна (в т.ч. с L1-корнями): декодится в Close. received = число кусков (>0).
    public int Accept(string rune)
    {
        if (Alphabet is null || RuneAlphabet is null) throw new Exception("CRIT: /connect was not called");

        if (_pieces.Count == 0) _assembly.Restart();

        _pieces.Add(new Piece(false, rune, 0));

        return _pieces.Count;
    }

    // Развязка-2: /g/<page> ставит страницу для следующего VF (адрес L3).
    public void SetFragmentPage(int page) => _pendingPage = page < 0 ? 0 : page;

    // VF по Развязке-1: /f/<offset>. Глобальный id = pendingPage*DfaSize + offset (для L2 pendingPage=0).
    // received = уровень: VFL2 (id<DfaSize) или VFL3 (id>=DfaSize).
    public int AcceptVirtualFragment(int offset)
    {
        if (Alphabet is null || RuneAlphabet is null) throw new Exception("CRIT: /connect was not called");

        if (_pieces.Count == 0) _assembly.Restart();

        int id = _pendingPage * DfaSize + offset;
        _pendingPage = 0;

        _pieces.Add(new Piece(true, "", id));

        return id < DfaSize ? VFL2 : VFL3;
    }

    // Развязка-3: сдвигает ПОСЛЕДНИЙ принятый VF на hops ёмкостей вперёд (id += hops*capacity).
    // Бесконечная строка своего печёного адреса не имеет и занимает его у финитной: клиент шлёт
    // якорь (id % capacity) обычным VF, а старший разряд (id / capacity) - этой развязкой.
    //
    // Цепь считаем, а не храним: звенья идут строго через ёмкость, так что Jump в БД это денормализация.
    public int Hop(int hops)
    {
        if (Alphabet is null || RuneAlphabet is null) throw new Exception("CRIT: /connect was not called");

        int capacity = DfaSize * PageCount;

        if (hops <= 0 || capacity <= 0 || _pieces.Count == 0) return VFBroken;

        var last = _pieces[^1];

        if (!last.IsFragment) return VFBroken;

        int id = last.FragmentId + hops * capacity;

        // Звена нет - цепь оборвалась. Кусок не трогаем: он останется тем, чем был, а недостающее
        // клиент дошлёт буквами.
        if (id >= _fragments.Count) return VFBroken;

        _pieces[^1] = new Piece(true, "", id);

        return VFInfinite;
    }

    public int SymbolsOf(TypeQuery type) => type == TypeQuery.Direct ? Alphabet!.Length : Translator.SymbolCount(Alphabet!);

    public AssembledResult Close(string tailText, TypeQuery type)
    {
        if (Alphabet is null || RuneAlphabet is null) throw new Exception("CRIT: /connect was not called");

        if (_pieces.Count == 0) _assembly.Restart();

        _assembly.Stop();

        sb.Clear();

        foreach (var piece in _pieces)
            sb.Append(piece.Text is not null
                ? piece.Text
                : piece.IsFragment
                    ? (piece.FragmentId >= 0 && piece.FragmentId < _fragments.Count ? _fragments[piece.FragmentId] : "")
                    : (type == TypeQuery.Direct
                        ? Translator.DirectUnrune(piece.Rune, RuneAlphabet, Alphabet, RuneSize)
                        : Translator.FragmentateUnrune(piece.Rune, RuneAlphabet, Alphabet, RuneSize, SymbolsOf(type))));

        sb.Append(tailText);

        // Кусок, оставшийся от промахнувшейся головы, приклеивается ПОСЛЕДНИМ - он и есть конец
        // адреса. Запрос за ним уже был оплачен головой, и терять его только потому, что она не
        // нашла адрес, значит брать за один кусок дважды.
        if (_trailing.Length > 0)
        {
            sb.Append(_trailing);

            _trailing = "";
        }

        int runes = _pieces.Count;

        // Разбивка покрытия: чем меньше chunks, тем плотнее словарь лёг на этот url.
        int capacity = DfaSize * PageCount;
        int chunks = 0, l2 = 0, l3 = 0, infinite = 0;

        foreach (var piece in _pieces)
        {
            if (!piece.IsFragment) { chunks++; continue; }

            if (piece.FragmentId < DfaSize) l2++;
            else if (piece.FragmentId < capacity) l3++;
            else infinite++;
        }

        // Цепочку кладём в дерево КАНОНИЧЕСКИ: путь считается по собранному адресу текущим
        // словарём, а не по тому, чем его продиктовали. Клиент с отстающим словарём диктует то же
        // самое рунами - и без канонизации завёл бы второй путь к тому же url, то есть дубль.
        //
        // Разбор идёт по ВСЕМУ адресу, включая хвостовые символы: лист обязан быть целым адресом,
        // иначе comments/1 и comments/2 сходятся в один узел и перетирают друг друга.
        string assembled = sb.ToString();

        (LastLeaf, LastPrefix, LastShared) = _tree.Remember(Canonical(assembled), assembled);

        _pieces.Clear();

        return new AssembledResult(sb.ToString(), runes, _assembly.ElapsedMilliseconds, chunks, l2, l3, infinite);
    }

    // Канонический разбор адреса текущим словарём: самый длинный фрагмент на каждой позиции,
    // всё непокрытое копится в один рунный шаг.
    //
    // Руны склеиваются целиком, а не режутся по RuneSize: размер руны - свойство клиента, а путь
    // в дереве обязан зависеть только от самого адреса. Иначе клиенты с разным RuneSize завели бы
    // разные пути к одному url.
    //
    // Шаг руны хранится РАЗЖАТЫМ: сравнивать надо содержимое, а не форму передачи, и тогда
    // неважно, чем кусок приехал - руной или фрагментом.
    public List<string> Canonical(string url)
    {
        // Последний сегмент пути - всегда ОТДЕЛЬНЫЙ шаг, каким бы длинным фрагментом словарь ни
        // накрывал его вместе с началом. Иначе семья распадается: "site.com/products/1" лежит в
        // словаре целиком и становится шагом от корня, а "site.com/products/7" разбирается как
        // "site.com/products/" + "7" - два брата у разных родителей. Прыжок отдаёт семью по
        // родителю, и четыре таких адреса уезжали четырьмя запросами вместо одного.
        int cut = url.LastIndexOf('/');

        if (cut <= 0 || cut + 1 >= url.Length) return Cover(url);

        var steps = Cover(url[..(cut + 1)]);

        steps.Add(HyperTree.StepOf(false, url[(cut + 1)..], 0));

        return steps;
    }

    // Разбор текста словарём: самый длинный фрагмент на каждой позиции, непокрытое - одним рунным шагом.
    private List<string> Cover(string url)
    {
        var steps = new List<string>();
        var runes = new System.Text.StringBuilder();

        int pos = 0;

        while (pos < url.Length)
        {
            int id = LongestAt(url, pos);

            if (id < 0)
            {
                runes.Append(url[pos]);
                pos++;

                continue;
            }

            if (runes.Length > 0) { steps.Add(HyperTree.StepOf(false, runes.ToString(), 0)); runes.Clear(); }

            steps.Add(HyperTree.StepOf(true, "", id));

            pos += _fragments[id].Length;
        }

        if (runes.Length > 0) steps.Add(HyperTree.StepOf(false, runes.ToString(), 0));

        return steps;
    }

    // Самый длинный фрагмент словаря, стоящий на этой позиции. -1 - ни один не подходит.
    private int LongestAt(string url, int pos)
    {
        int best = -1, length = 0;

        for (int id = 0; id < _fragments.Count; id++)
        {
            string text = _fragments[id];

            // Отсечка по первому символу до сравнения: словарь в десятки тысяч строк, и без неё
            // разбор одного адреса стоил бы миллионы сравнений.
            if (text.Length <= length || text.Length == 0 || text[0] != url[pos]) continue;
            if (pos + text.Length > url.Length) continue;
            if (string.CompareOrdinal(url, pos, text, 0, text.Length) != 0) continue;

            best = id;
            length = text.Length;
        }

        return best;
    }

    public int Intern(string url, long firstSendMs)
    {
        if (_byUrl.TryGetValue(url, out int existing)) return existing;

        int handle = _handles.Count;

        _handles.Add(url);
        _firstSendMs.Add(firstSendMs);
        _byUrl[url] = handle;

        return handle;
    }

    // Сброс хайперов (dev): собранный url перестаёт отдаваться одним /h/ и снова идёт сборкой.
    public void ForgetHypers()
    {
        _tree.Forget();

        _handles.Clear();
        _byUrl.Clear();
        _firstSendMs.Clear();
    }

    public string? Resolve(int handle) => handle >= 0 && handle < _handles.Count ? _handles[handle] : null;

    public long FirstSendMsOf(int handle) => handle >= 0 && handle < _firstSendMs.Count ? _firstSendMs[handle] : -1;

    public string? ResolveVirtualFragment(int id) => id >= 0 && id < _fragments.Count ? _fragments[id] : null;

    public IReadOnlyList<string> HyperUrls => _handles;

    public IReadOnlyList<string> FragmentTexts => _fragments;

    // Классический LZW поверх символов собранного payload'а. _phrases растёт на всё, а в адресную
    // таблицу _fragments фраза попадает, дорастив до FragmentMinLength.
    //
    // Адреса выдаём подряд и на потолке L3 не останавливаемся: за ним начинается Infinite, который
    // клиент достаёт Развязкой-3 (якорь + hop). Поэтому адресуемо не DfaSize*PageCount, а всё
    // до DfaSize*PageCount*HopCount - такие строки едут клиенту как обычно.
    //
    // Overflowed - то, что не адресуется даже так: в БД строка ляжет, но клиенту не пойдёт, он её
    // не адресует и пошлёт буквами (direct-фоллбэк). Сохранена она уже сейчас, и подхватится, когда
    // адресное пространство вырастет.
    public LearnResult LearnFrom(string text)
    {
        var addressable = new List<FragmentSeed>();
        var overflowed = new List<FragmentSeed>();

        if (DfaSize <= 0 || string.IsNullOrEmpty(text)) return new LearnResult(addressable, overflowed);

        int limit = DfaSize * PageCount * HopCount;

        string w = "";

        foreach (char c in text)
        {
            string wc = w + c;

            if (wc.Length == 1 || _phrases.Contains(wc)) { w = wc; continue; }

            _phrases.Add(wc);

            if (wc.Length >= FragmentMinLength && !_fragIndex.ContainsKey(wc))
            {
                int id = _fragments.Count;

                _fragments.Add(wc);
                _fragIndex[wc] = id;

                (id < limit ? addressable : overflowed).Add(new FragmentSeed(id, wc));
            }

            w = c.ToString();
        }

        return new LearnResult(addressable, overflowed);
    }

    public void PushDirectRunes(string runes) => DirectRunes = direct.Append(runes).ToString();

    public void PushDirect(string runes) => DirectUnruned = unruned.Append(runes).ToString();

    public void Foget()
    {
        sb.Clear();
        direct.Clear();
        unruned.Clear();

        DirectRunes = string.Empty;
        DirectUnruned = string.Empty;
    }
}
