using System.Diagnostics;
using System.Text;
using CombineQueries.Domain.Aggregates.Translator;
using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Services.Speech;

public class Speach : ISpeech
{
    public string? Alphabet { get; private set; }
    public string? RuneAlphabet { get; private set; }
    public int RuneSize { get; private set; } = 3;
    public string Scheme { get; private set; } = "https";
    public string DirectRunes { get; set; } = string.Empty;
    public string DirectUnruned { get; set; } = string.Empty;
    public bool Authorized { get; private set; }

    private readonly List<string> _runes = [];
    private readonly List<string> _handles = [];
    private readonly List<long> _firstSendMs = [];
    private readonly Dictionary<string, int> _byUrl = [];
    private readonly Stopwatch _assembly = new();
    private readonly StringBuilder sb = new();
    private readonly StringBuilder direct = new();
    private readonly StringBuilder unruned = new();

    private string _authBuffer = "";

    private const int AuthMax = 128;

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

        _runes.Clear();
    }

    public int Accept(string rune)
    {
        if (Alphabet is null || RuneAlphabet is null) throw new Exception("CRIT: /init was not called");

        if (_runes.Count == 0) _assembly.Restart();

        _runes.Add(rune);

        return _runes.Count;
    }

    public int SymbolsOf(TypeQuery type) => type == TypeQuery.Direct ? Alphabet!.Length : Translator.SymbolCount(Alphabet!);

    public AssembledResult Close(string tailText, TypeQuery type)
    {
        if (Alphabet is null || RuneAlphabet is null) throw new Exception("CRIT: /init was not called");

        if (_runes.Count == 0) _assembly.Restart();

        _assembly.Stop();

        sb.Clear();

        foreach (var rune in _runes)
            sb.Append(type == TypeQuery.Direct
                ? Translator.DirectUnrune(rune, RuneAlphabet, Alphabet, RuneSize)
                : Translator.FragmentateUnrune(rune, RuneAlphabet, Alphabet, RuneSize, SymbolsOf(type)));

        sb.Append(tailText);

        int runes = _runes.Count;

        _runes.Clear();

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
