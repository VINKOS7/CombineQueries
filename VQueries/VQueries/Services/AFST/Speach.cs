using System.Diagnostics;

using CombineQueries.Domain.Aggregates.Translator;
using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Services.AFST;

public class Speach : ISpeach
{
    public string? Alphabet { get; private set; }
    public string? RuneAlphabet { get; private set; }
    public int RuneSize { get; private set; } = 3;
    public string Scheme { get; private set; } = "https";

    private readonly List<string> _runes = new();
    private readonly Stopwatch _assembly = new();

    private readonly List<string> _handles = new();
    private readonly Dictionary<string, int> _byUrl = new();
    private readonly List<long> _firstSendMs = new();

    public void SetContext(ISetContextCommand<char> command)
    {
        Alphabet = command.Alphabet;
        RuneAlphabet = Domain.Aggregates.Translator.Translator.RuneAlphabetOf(command.Alphabet);
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

    public int SymbolsOf(bool direct) =>
        direct ? Alphabet!.Length : Domain.Aggregates.Translator.Translator.SymbolCount(Alphabet!);

    public AssembledResult Close(string tailText, bool direct)
    {
        if (Alphabet is null || RuneAlphabet is null) throw new Exception("CRIT: /init was not called");

        if (_runes.Count == 0) _assembly.Restart();

        _assembly.Stop();

        var sb = new System.Text.StringBuilder();

        foreach (var rune in _runes)
            sb.Append(Domain.Aggregates.Translator.Translator.DecodeRune(rune, RuneAlphabet, Alphabet, RuneSize, SymbolsOf(direct)));

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
}
