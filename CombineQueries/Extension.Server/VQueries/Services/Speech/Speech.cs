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

    public void RestoreChains(IEnumerable<(int Id, int? ParentId, string Step, string? Url)> nodes) => _tree.Restore(nodes);

    // Сид хайпера для клиента: адрес -> номер прыжка. Только листы, промежуточные узлы фронту
    // не нужны - он держит плоский словарь, а не дерево.
    public IEnumerable<(string Url, int Jump)> ChainLeaves() => _tree.Leaves();

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

            // Шаг закодирован как "f<адрес>" либо "r<руны>" - разбираем обратно в кусок сборки.
            if (step[0] == 'f' && int.TryParse(step[1..], out int id)) _pieces.Add(new Piece(true, "", id));
            else _pieces.Add(new Piece(false, step[1..], 0));
        }

        return _pieces.Count;
    }

    public string? UrlOf(int handle) => _tree.UrlOf(handle);

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

    private readonly record struct Piece(bool IsFragment, string Rune, int FragmentId);

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

    private int _signPos;

    public const int SignValues = 8;

    private const int SignLength = 256;

    // Сверяет очередную подпись и продвигает позицию. Последовательность идёт по кругу: длины
    // хватает, чтобы наблюдение за одним URL не выдавало следующую.
    public bool CheckSign(int sign)
    {
        if (Signs.Length == 0) return true;

        bool ok = sign >= 0 && sign < SignValues && Signs[_signPos] - '0' == sign;

        _signPos = (_signPos + 1) % Signs.Length;

        return ok;
    }

    private static string NewSigns()
    {
        var signs = new char[SignLength];

        for (int i = 0; i < SignLength; i++) signs[i] = (char)('0' + System.Security.Cryptography.RandomNumberGenerator.GetInt32(SignValues));

        return new string(signs);
    }

    public void Authorize() => Authorized = true;

    // Неверный запрос в потоке валит приём целиком: собранное выбрасываем, дальше не принимаем.
    //
    // Иначе битое звено просто выпадало бы из сборки, и наружу уехал бы URL, который никто не
    // запрашивал - а это уже не сбой, а переход по чужому адресу. Чинится только повторным connect:
    // поток последовательный, доверять его середине после сбоя нельзя.
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

        // Чистим только незавершённую сборку. Хайперы и фрагменты НЕ трогаем: при реконнекте
        // (повторный connect) они остаются тёплыми и уезжают сидом.
        _pieces.Clear();
        _pendingPage = 0;

        // Connect - единственный способ снять срыв приёма. Заодно рождается новая последовательность
        // подписей: старая после сбоя могла быть подсмотрена.
        Broken = false;
        LastFault = "";

        Signs = NewSigns();
        _signPos = 0;
    }

    // Заливка тёплого словаря из персиста (вызывается на connect, после SetContext). Индекс списка
    // здесь И ЕСТЬ адрес/handle, поэтому дырки в нумерации добиваем пустыми - иначе всё поедет.
    // Сиды ждём упорядоченными по id/handle.
    public void Restore(IReadOnlyList<FragmentSeed> fragments, IReadOnlyList<HyperSeed> hypers)
    {
        _fragments.Clear();
        _fragIndex.Clear();
        _phrases.Clear();

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
            sb.Append(piece.IsFragment
                ? (piece.FragmentId >= 0 && piece.FragmentId < _fragments.Count ? _fragments[piece.FragmentId] : "")
                : (type == TypeQuery.Direct
                    ? Translator.DirectUnrune(piece.Rune, RuneAlphabet, Alphabet, RuneSize)
                    : Translator.FragmentateUnrune(piece.Rune, RuneAlphabet, Alphabet, RuneSize, SymbolsOf(type))));

        sb.Append(tailText);

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

        // Цепочку кладём в дерево до очистки: путь от корня и есть поток запросов этого url.
        // Leaf - прыжок на весь адрес, Prefix - докуда он совпал с уже известными.
        (LastLeaf, LastPrefix, LastShared) = _tree.Remember(StepsOf(), sb.ToString());

        _pieces.Clear();

        return new AssembledResult(sb.ToString(), runes, _assembly.ElapsedMilliseconds, chunks, l2, l3, infinite);
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
