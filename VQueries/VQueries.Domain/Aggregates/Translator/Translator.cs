using Dotseed.Domain;
using CombineQueries.Domain.Aggregates.Translator.types;
using System.Text;

namespace CombineQueries.Domain.Aggregates.Translator;

public class Translator : Entity, IAggregateRoot
{
    
    public Guid Id { get; set; } = new();
    public required string BaseForwardUrl { get; set; }
    public required string Alphabet { get; set; }
    public required IArenaTreeRunes<char> Runes { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }

    private static readonly StringBuilder sb = new ();

    private int BaseRune { get; set; }

    public static Translator From(IAddTranslator<char> command) => new()
    {
        Alphabet = command.Alphabet,
        BaseForwardUrl = command.BaseForwardUrl,
        Runes = command.Runes,

        Name = command.Name ?? string.Empty,
        Description = command.Description ?? string.Empty,
        BaseRune = BaseForRune(command.SizeRune + 1)
    };

    public static IArenaTreeRunes<char> ATRFrom(string alphabet)
    {
        var arena = new ArenaTreeRunes<char>();

        foreach (char c in alphabet) arena.From(arena.Root, c);

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
        foreach (char c in alphabet) if (UrlUnsafe.IndexOf(c) < 0) sb.Clear().Append(c);

        return sb.ToString();
    }

    public static string DecodeRune(string rune, string runeAlphabet, string alphabet, int runeSize, int symbols)
    {
        long value = 0;

        foreach (char c in rune)
        {
            int digit = runeAlphabet.IndexOf(c);

            if (digit < 0) throw new Exception($"domain error: rune symbol '{c}' is not in rune alphabet");

            value = value * runeAlphabet.Length + digit;
        }

        var parts = new string[runeSize];

        for (int i = runeSize - 1; i >= 0; i--)
        {
            parts[i] = SymbolOf(alphabet, (int) (value % symbols));
            value /= symbols;
        }

        return string.Concat(parts);
    }

    public const char Pad = ':';

    public static string FragmentateUnrune(string rune, string runeAlphabet, string alphabet, int runeSize, int symbols)
    {
        string text = DecodeRune(rune, runeAlphabet, alphabet, runeSize, symbols);

        int cut = 0;

        while (cut < runeSize && cut < text.Length && text[text.Length - 1 - cut] == Pad) cut++;

        return text.Substring(0, text.Length - cut);
    }

    private static int BaseForRune(int runeSize)
    {
        if (runeSize < 1) return 0;
        if (runeSize == 1) return int.MaxValue;

        int lo = 1, hi = 46340; // 46340^2 - предел даже для руны из двух разрядов

        while (lo < hi)
        {
            int mid = lo + (hi - lo + 1) / 2;

            if (FitsInInt(mid, runeSize)) lo = mid;
            else hi = mid - 1;
        }

        return lo;
    }

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

        sb.Clear();

        foreach (int id in input)
        {
            int rest = id;

            for (int k = groupSize - 1; k >= 0; k--)
            {
                block[k] = alphabet[rest % baseRune];
                rest /= baseRune;
            }

            sb.Append(block);
        }

        return sb.ToString();
    }

    public static string DirectUnrune(string input, string alphabet, int groupSize, int baseRune)
    {
        if (input == null || input.Length == 0 || groupSize < 1) return "";

        char[] block = new char[groupSize];

        sb.Clear();

        foreach (int id in input)
        {
            int rest = id;

            for (int k = groupSize - 1; k >= 0; k--)
            {
                block[k] = alphabet[rest % baseRune];
                rest /= baseRune;
            }

            sb.Append(block);
        }

        return sb.ToString();
    }

    public static TypeCombine TypeFrom<TRune> (TRune symbol, string alphabet) where TRune : struct=> Func<bool> switch
    {
        _ when IsFragmentate(RuneFrom(symbol), alphabet) => TypeCombine.Fragmentate,
        _ when IsFragmentate(RuneFrom(symbol), alphabet) => TypeCombine.Fragmentate,
        _ when IsDirect(RuneFrom(symbol), alphabet) => TypeCombine.Direct,
        _ => throw new Exception($"domain error: unknown type symbol '{symbol}'")
    };

    private static bool FitsInInt(int b, int runeSize)
    {
        long limit = (long)int.MaxValue + 1;
        long p = 1;

        for (int i = 0; i < runeSize; i++)
        {
            p *= b;

            if (p > limit) return false;
        }

        return true;
    }

    private static char RuneFrom<TRune>(TRune symbol) where TRune : struct => symbol switch
    {
        char c => c,
        int i => (char)i,
        _ => throw new Exception($"domain error: unsupported rune type '{typeof(TRune)}'")
    };

    private static bool IsFragmentate(int index, string alphabet) => index >= alphabet.Length;
    private static bool IsFragmentate(char symbol, string alphabet) => alphabet.IndexOf(symbol) < 0;
    private static bool IsDirect(int index, string alphabet) => index >= alphabet.Length;


}
