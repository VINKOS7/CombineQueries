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
