using Dotseed.Domain;
using CombineQueries.Domain.Aggregates.Translator.types;
using System.Text;

namespace CombineQueries.Domain.Aggregates.Translator;

public class Translator : Entity, IAggregateRoot
{
    
    public new Guid Id { get; set; } = new();
    public required string BaseForwardUrl { get; set; }
    public required string Alphabet { get; set; }
    public required IArenaTreeRunes<char> Runes { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }

    // Свой словарь VF, связь 1 ко многим. Не путать со статическим Fragments ниже: там L1-корни
    // рун-алфавита (печёные, курируемые), а здесь адресуемые строки, которые растит обучение.
    public ICollection<VirtualFragment> VirtualFragments { get; set; } = [];

    // Свои хайперы, связь 1 ко многим: handle -> собранный URL.
    public ICollection<Hyper> Hypers { get; set; } = [];

    // Дерево цепочек combine-запросов: узлы ссылаются на родителя внутри этой же коллекции.
    public ICollection<Chain> Chains { get; set; } = [];

    // Кладёт узел дерева. Дубль по номеру отбиваем - номера назначает дерево в рантайме.
    public Chain? Grow(int id, int? parentId, string step, string? url)
    {
        if (id < 0 || string.IsNullOrEmpty(step)) return null;

        foreach (var known in Chains)
            if (known.Id == id)
            {
                // Узел уже есть: у листа мог появиться адрес, это единственное, что меняется.
                if (url is not null) known.Url = url;

                return known;
            }

        var chain = new Chain { Id = id, TranslatorId = Id, ParentId = parentId, Step = step, Url = url };

        Chains.Add(chain);

        return chain;
    }

//    private int BaseRune { get; set; }

    public static Translator From(IAddTranslator<char> command) => new()
    {
        Alphabet = command.Alphabet,
        BaseForwardUrl = command.BaseForwardUrl,
        Runes = command.Runes,

        Name = command.Name ?? string.Empty,
        Description = command.Description ?? string.Empty
//      BaseRune = BaseForRune(command.SizeRune + 1)
    };

    public static IArenaTreeRunes<char> ATRFrom(string alphabet)
    {
        var arena = new ArenaTreeRunes<char>();

        foreach (char c in alphabet) arena.From(arena.Root!, c);

        return arena;
    }

    public static readonly string[] Fragments =
    [
        "/todos/", "localhost:", "www.", ".com", ".org", ".net", ".ru", ".io", ".dev",
        "/api/", "/v1/", "/r/", "/comments/", ".html", ".php", ".json",
        "json", "html", "index", "search", "image", "video", "data", "list", "item",
        "page", "user", "admin", "name", "true", "false", "?id=", "&id=", "/users/", "com"
    ];

    // Кладёт строку словаря по её глобальному адресу; уровень считает адрес и размеры развязок.
    // Словарь - биекция текст<->адрес, поэтому дубль по любой из сторон отбиваем (вернём null).
    public VirtualFragment? Learn(int id, string text, int dfaSize, int pageCount)
    {
        if (string.IsNullOrEmpty(text) || id < 0) return null;

        foreach (var known in VirtualFragments) if (known.Id == id || known.Text == text) return null;

        var fragment = new VirtualFragment
        {
            Id = id,
            TranslatorId = Id,
            Text = text,
            Level = VirtualFragment.LevelOf(id, dfaSize, pageCount)
        };

        VirtualFragments.Add(fragment);

        return fragment;
    }

    // Запоминает собранный URL под его handle. Дубль по любой из сторон отбиваем: handle<->url
    // такая же биекция, как адрес<->текст у словаря.
    public Hyper? Remember(int handle, string url)
    {
        if (string.IsNullOrEmpty(url) || handle < 0) return null;

        foreach (var known in Hypers) if (known.Id == handle || known.Url == url) return null;

        var hyper = new Hyper { Id = handle, TranslatorId = Id, Url = url };

        Hypers.Add(hyper);

        return hyper;
    }

    // Сшивает Infinite-строки в цепочки «заёма по одному фрагменту».
    //
    // Бесконечная строка своего печёного адреса не имеет и ЗАНИМАЕТ его у финитной: якорь =
    // id % capacity, а следующее звено той же цепи лежит ровно через ёмкость - Jump = id + capacity.
    // Значит адрес раскладывается на «якорь + k хопов», где k = id / capacity, и каждый хоп стоит
    // ровно один запрос. End держит готовый результат, чтобы tail не проходил цепь заново.
    public void ChainInfinite(int capacity)
    {
        if (capacity <= 0) return;

        var byId = new Dictionary<int, VirtualFragment>();

        foreach (var fragment in VirtualFragments) byId[fragment.Id] = fragment;

        foreach (var fragment in VirtualFragments)
        {
            if (fragment.Level != FragmentLevel.Infinite) continue;

            fragment.Jump = byId.ContainsKey(fragment.Id + capacity) ? fragment.Id + capacity : null;
            fragment.End = fragment.Text;
        }
    }

    public static int SymbolCount(string alphabet) => alphabet.Length + Fragments.Length;

    public static string SymbolOf(string alphabet, int index) => index < alphabet.Length ? alphabet[index].ToString() : Fragments[index - alphabet.Length];

    public const string UrlUnsafe = "#%[]/?";

    public static string RuneAlphabetOf(string alphabet)
    {
        var runeAlphabet = new StringBuilder();

        foreach (char c in alphabet) if (UrlUnsafe.IndexOf(c) < 0) runeAlphabet.Append(c);

        return runeAlphabet.ToString();
    }

    public static long ValueOf(string rune, string runeAlphabet)
    {
        long value = 0;

        foreach (char c in rune)
        {
            int digit = runeAlphabet.IndexOf(c);

            if (digit < 0) throw new Exception($"domain error: rune symbol '{c}' is not in rune alphabet");

            value = value * runeAlphabet.Length + digit;
        }

        return value;
    }

    public static int[] IndexesOf(string rune, string runeAlphabet, int runeSize, int symbols)
    {
        long value = ValueOf(rune, runeAlphabet);
        var indexes = new int[runeSize];

        for (int i = runeSize - 1; i >= 0; i--)
        {
            indexes[i] = (int)(value % symbols);
            value /= symbols;
        }

        return indexes;
    }

    public static bool IsFragment(int index, string alphabet) => index >= alphabet.Length;

    public static bool HasFragment(string rune, string runeAlphabet, string alphabet, int runeSize, int symbols)
    {
        foreach (int index in IndexesOf(rune, runeAlphabet, runeSize, symbols)) if (IsFragment(index, alphabet)) return true;

        return false;
    }

    public static readonly string[] DirectFragments = ["", "o", ".com/", "."];

    public static string FragmentateUnrune(string rune, string runeAlphabet, string alphabet, int runeSize, int symbols)
    {
        int[] indexes = IndexesOf(rune, runeAlphabet, runeSize, symbols);
        var parts = new string[runeSize];

        for (int i = 0; i < runeSize; i++) parts[i] = SymbolOf(alphabet, indexes[i]);

        return string.Concat(parts);
    }

    public static string DirectUnrune(string rune, string runeAlphabet, string alphabet, int chars)
    {
        long value = ValueOf(rune, runeAlphabet);

        int piece = (int)(value % DirectFragments.Length);
        value /= DirectFragments.Length;

        var text = new char[chars];

        for (int i = chars - 1; i >= 0; i--)
        {
            text[i] = alphabet[(int)(value % alphabet.Length)];
            value /= alphabet.Length;
        }

        return new string(text) + DirectFragments[piece];
    }

    public const char Pad = ':';

    public static string TrimPad(string text, int runeSize)
    {
        int cut = 0;

        while (cut < runeSize && cut < text.Length && text[text.Length - 1 - cut] == Pad) cut++;

        return text[..^cut];
    }

//    private static int BaseForRune(int runeSize)
//    {
//        if (runeSize < 1) return 0;
//        if (runeSize == 1) return int.MaxValue;
//
//        int lo = 1, hi = 46340; // 46340^2 - предел даже для руны из двух разрядов
//
//        while (lo < hi)
//        {
//            int mid = lo + (hi - lo + 1) / 2;
//
//            if (FitsInInt(mid, runeSize)) lo = mid;
//            else hi = mid - 1;
//        }
//
//        return lo;
//    }

    public static int[] Compress(string input, string alphabet, int group, int baseRune)
    {
        if (string.IsNullOrEmpty(input) || group < 1) return [];

        if (input.Length % group != 0) return [];

        int n = input.Length / group;
        int[] res = new int[n];

        for (int b = 0; b < n; b++)
        {
            int acc = 0;

            for (int k = 0; k < group; k++)
            {
                int idx = alphabet.IndexOf(input[b * group + k]);

                if (idx < 0) return [];

                acc = acc * baseRune + idx;
            }

            res[b] = acc;
        }

        return res;
    }

    public static string Decompress(int[] input, string alphabet, int groupSize, int baseRune)
    {
        if (input == null || input.Length == 0 || groupSize < 1) return "";

        char[] block = new char[groupSize];
        var text = new StringBuilder();

        foreach (int id in input)
        {
            int rest = id;

            for (int k = groupSize - 1; k >= 0; k--)
            {
                block[k] = alphabet[rest % baseRune];
                rest /= baseRune;
            }

            text.Append(block);
        }

        return text.ToString();
    }

//    public static TypeCombine TypeFrom<TRune>(TRune symbol, string alphabet) where TRune : notnull => true switch
//    {
//        _ when IsFragmentate(RuneFrom(symbol), alphabet) => TypeCombine.Fragmentate,
//        _ when IsDirect(RuneFrom(symbol), alphabet) => TypeCombine.Direct,
//        _ => throw new Exception($"domain error: unknown type symbol '{symbol}'")
//    };

//    private static bool FitsInInt(int b, int runeSize)
//    {
//        long limit = (long)int.MaxValue + 1;
//        long p = 1;
//
//        for (int i = 0; i < runeSize; i++)
//        {
//            p *= b;
//
//            if (p > limit) return false;
//        }
//
//        return true;
//    }

//    private static char RuneFrom<TRune>(TRune symbol) where TRune : notnull => symbol switch
//    {
//        char c => c,
//        int i => (char)i,
//        _ => throw new Exception($"domain error: unsupported rune type '{typeof(TRune)}'")
//    };
//
//    private static bool IsFragmentate(char symbol, string alphabet) => alphabet.IndexOf(symbol) < 0;
//    private static bool IsDirect(int index, string alphabet) => index >= alphabet.Length;


}
