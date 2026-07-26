using System.Diagnostics;

using CombineQueries.Domain.Aggregates.Translator;
using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Services.AFST;

// Singleton. Держит состояние МЕЖДУ запросами: алфавит, дерево (на будущее), буфер сборки
// и таблицу хэндлов. Scoped здесь ломает всё - контекст /init теряется сразу же.
public class AFST : IAFST
{
    public string? Alphabet { get; private set; }
    public string? WireAlphabet { get; private set; }
    public IArenaTreeRunes<char>? ArenaTreeContext { get; private set; }

    public IList<string> CombineRunes { get; } = new List<string>();

    public int RuneSize { get; private set; } = 3;

    // сколько кусков ждём и сколько символов срезать с хвоста после склейки
    private int expected;
    private int pad;

    // Секундомер всей отправки: стартует на /n, снимается на последнем куске.
    // Меряет серверную сторону - паузы между запросами Udon сюда попадают, а сам round-trip нет.
    private readonly Stopwatch _assembly = new();

    // хэндл -> ссылка и обратно. Живут в памяти: рестарт сервера их обнуляет,
    // поэтому у клиента обязана быть ветка "хэндл неизвестен -> шлю полностью".
    private readonly List<string> _handles = new();
    private readonly Dictionary<string, int> _byUrl = new();

    // время ПЕРВОЙ (полной) отправки по каждому хэндлу - эталон для сравнения с быстрым путём
    private readonly List<long> _firstSendMs = new();

    public void SetContext(ISetContextCommand<char> command)
    {
        Alphabet = command.Alphabet;
        WireAlphabet = Domain.Aggregates.Translator.Translator.WireAlphabetOf(command.Alphabet);
        ArenaTreeContext = command.ArenaTreeContext;
        RuneSize = command.RuneSize;

        CombineRunes.Clear();

        expected = 0;
        pad = 0;

        // хэндлы НЕ чистим: /init может вызываться при перезаходе клиента, а кэш ссылок переживает
    }

    public void Expect(int runeCount, int padCount)
    {
        expected = runeCount;
        pad = padCount;

        CombineRunes.Clear();

        _assembly.Restart();
    }

    public RuneResult Accept(string wireRune)
    {
        if (Alphabet is null || WireAlphabet is null) throw new Exception("CRIT: /init was not called");

        CombineRunes.Add(wireRune);

        int received = CombineRunes.Count;

        if (expected <= 0 || received < expected)
            return new RuneResult(false, received, expected, null, _assembly.ElapsedMilliseconds);

        _assembly.Stop();

        var sb = new System.Text.StringBuilder();

        foreach (var rune in CombineRunes)
            sb.Append(Domain.Aggregates.Translator.Translator.DecodeRune(rune, WireAlphabet, Alphabet, RuneSize));

        string text = sb.ToString();

        // добивку срезаем по объявленному числу, не по содержимому - иначе срежем настоящий символ
        if (pad > 0 && pad <= text.Length) text = text.Substring(0, text.Length - pad);

        CombineRunes.Clear();
        expected = 0;
        pad = 0;

        return new RuneResult(true, received, received, text, _assembly.ElapsedMilliseconds);
    }

    public int Intern(string url, long firstSendMs)
    {
        // Эталон первой отправки НЕ перетираем: он тем и ценен, что снят до всякого кэша.
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
