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
    public string DirectRunes { get; set; } = string.Empty;
    public string DirectUnruned { get; set; } = string.Empty;
    public bool Authorized { get; private set; }

    // Поток сборки - упорядоченный: между рун-кусками (чанк) вклиниваются виртуальные фрагменты (VF).
    // Кусок либо руна (декод в Close по типу хвоста), либо VF (готовый текст по id). Запросы Udon
    // последовательны, порядок прихода = порядок в URL.
    private readonly List<Piece> _pieces = [];

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

    private readonly Stopwatch _assembly = new();
    private readonly StringBuilder sb = new();
    private readonly StringBuilder direct = new();
    private readonly StringBuilder unruned = new();

    private string _authBuffer = "";

    private const int AuthMax = 128;

    private readonly record struct Piece(bool IsFragment, string Rune, int FragmentId);

    public void Authorize() => Authorized = true;

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

        // Чистим только незавершённую сборку. Хайперы и фрагменты НЕ трогаем: при реконнекте
        // (повторный /init) они остаются тёплыми и уезжают сидом.
        _pieces.Clear();
        _pendingPage = 0;
    }

    // Обычный чанк-руна (в т.ч. с L1-корнями): декодится в Close. received = число кусков (>0).
    public int Accept(string rune)
    {
        if (Alphabet is null || RuneAlphabet is null) throw new Exception("CRIT: /init was not called");

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
        if (Alphabet is null || RuneAlphabet is null) throw new Exception("CRIT: /init was not called");

        if (_pieces.Count == 0) _assembly.Restart();

        int id = _pendingPage * DfaSize + offset;
        _pendingPage = 0;

        _pieces.Add(new Piece(true, "", id));

        return id < DfaSize ? VFL2 : VFL3;
    }

    public int SymbolsOf(TypeQuery type) => type == TypeQuery.Direct ? Alphabet!.Length : Translator.SymbolCount(Alphabet!);

    public AssembledResult Close(string tailText, TypeQuery type)
    {
        if (Alphabet is null || RuneAlphabet is null) throw new Exception("CRIT: /init was not called");

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

        _pieces.Clear();

        return new AssembledResult(sb.ToString(), runes, _assembly.ElapsedMilliseconds);
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

    public string? Resolve(int handle) => handle >= 0 && handle < _handles.Count ? _handles[handle] : null;

    public long FirstSendMsOf(int handle) => handle >= 0 && handle < _firstSendMs.Count ? _firstSendMs[handle] : -1;

    public string? ResolveVirtualFragment(int id) => id >= 0 && id < _fragments.Count ? _fragments[id] : null;

    public IReadOnlyList<string> HyperUrls => _handles;

    public IReadOnlyList<string> FragmentTexts => _fragments;

    // Классический LZW поверх символов собранного payload'а. _phrases растёт на всё, а в адресную
    // таблицу _fragments фраза попадает, дорастив до FragmentMinLength и пока есть адреса
    // (< DfaSize*PageCount: L2+L3). Возвращаем новоприбывшие адресуемые фрагменты - пиггибэк в /t/.
    public IReadOnlyList<FragmentSeed> LearnFrom(string text)
    {
        var learned = new List<FragmentSeed>();

        if (DfaSize <= 0 || string.IsNullOrEmpty(text)) return learned;

        int capacity = DfaSize * PageCount;

        string w = "";

        foreach (char c in text)
        {
            string wc = w + c;

            if (wc.Length == 1 || _phrases.Contains(wc)) { w = wc; continue; }

            _phrases.Add(wc);

            if (wc.Length >= FragmentMinLength && _fragments.Count < capacity && !_fragIndex.ContainsKey(wc))
            {
                int id = _fragments.Count;

                _fragments.Add(wc);
                _fragIndex[wc] = id;

                learned.Add(new FragmentSeed(id, wc));
            }

            w = c.ToString();
        }

        return learned;
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
