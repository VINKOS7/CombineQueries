using CombineQueries.Domain.Aggregates.Translator;
using CombineQueries.Domain.Aggregates.Translator.types;
using System.Data;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Text;

namespace CombineQueries.Api.Services.Speech;

public class Speach : ISpeech
{
    public string? Alphabet { get; private set; }
    public string? RuneAlphabet { get; private set; }
    public int RuneSize { get; private set; } = 3;
    public string Scheme { get; private set; } = "https";
    public int DfaSize { get; private set; }
    public string DirectRunes { get; set; } = string.Empty;
    public string DirectUnruned { get; set; } = string.Empty;
    public bool Authorized { get; private set; }

    // Поток сборки - упорядоченный, потому что между рун-кусками (/c/) вклиниваются
    // динамические фрагменты (/f/). Кусок либо руна (декодится в Close по типу хвоста),
    // либо ссылка на фрагмент (готовый литеральный текст). Запросы Udon строго
    // последовательны, так что порядок прихода = порядок в URL.
    private readonly List<Piece> _pieces = [];

    private readonly List<string> _handles = [];
    private readonly List<long> _firstSendMs = [];
    private readonly Dictionary<string, int> _byUrl = [];

    // DF-словарь. _phrases - весь LZW-словарь фраз (для роста), _fragments - клиентская
    // таблица адресов (только фразы >= FragmentMinLength, не больше DfaSize): её id клиент
    // печёт в пул /f/ и её же получает сидом в connect. Server-authoritative: клиент только
    // зеркалит id->text, свой LZW не считает.
    private readonly HashSet<string> _phrases = [];
    private readonly List<string> _fragments = [];
    private readonly Dictionary<string, int> _fragIndex = [];

    private const int FragmentMinLength = 6;

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

        // Чистим только незавершённую сборку. Хайперы и фрагменты НЕ трогаем: при
        // реконнекте клиента (повторный /init) они остаются тёплыми и уезжают сидом.
        _pieces.Clear();
    }

    public int Accept(string rune, LambdaExpression lambda) => Accept(() => _pieces.Add(new Piece(false, rune, 0)));

    public int AcceptVirtualFragment(int id) => Accept("", () => _pieces.Add(new Piece(true, "", id)));

    public int SymbolsOf(TypeQuery type) => type == TypeQuery.Direct ? Alphabet!.Length : Translator.SymbolCount(Alphabet!);

    public AssembledResult Close(string tailText, TypeQuery type)
    {
        if (Alphabet is null || RuneAlphabet is null) throw new Exception("CRIT: /init was not called");

        if (_pieces.Count == 0) _assembly.Restart();

        _assembly.Stop();

        sb.Clear();

        foreach (var piece in _pieces) sb.Append(piece.IsFragment
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

    public void PushDirectRunes(string runes) => DirectRunes = direct.Append(runes).ToString();
    public void PushDirect(string runes) => DirectUnruned = unruned.Append(runes).ToString();
    public long FirstSendMsOf(int handle) => handle >= 0 && handle < _firstSendMs.Count ? _firstSendMs[handle] : -1;
    public string? Resolve(int handle) => handle >= 0 && handle < _handles.Count ? _handles[handle] : null;
    public string? ResolveVirtualFragment(int id) => id >= 0 && id < _fragments.Count ? _fragments[id] : null;
    public IReadOnlyList<string> HyperUrls => _handles;
    public IReadOnlyList<string> FragmentTexts => _fragments;

    public IReadOnlyList<FragmentSeed> LearnFrom(string text)
    {
        var learned = new List<FragmentSeed>();

        if (DfaSize <= 0 || string.IsNullOrEmpty(text)) return learned;

        string w = "";

        foreach (char c in text)
        {
            string wc = w + c;

            if (wc.Length == 1 || _phrases.Contains(wc)) { w = wc; continue; }

            _phrases.Add(wc);

            if (wc.Length >= FragmentMinLength && _fragments.Count < DfaSize && !_fragIndex.ContainsKey(wc))
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

    public void Foget()
    {
        sb.Clear();
        direct.Clear();
        unruned.Clear();

        DirectRunes = string.Empty;
        DirectUnruned = string.Empty;
    }

    private int Accept(Action lambda)
    {
        if (Alphabet is null || RuneAlphabet is null) throw new Exception("CRIT: /init was not called");

        if (_pieces.Count == 0) _assembly.Restart();

        lambda();

        return _pieces.Count;
    }
}
