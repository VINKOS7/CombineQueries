using Dotseed.Domain;
using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Domain.Aggregates.Translator;

public class Translator : Entity, IAggregateRoot
{
    public Guid Id { get; set; } = new();

    public required string BaseForwardUrl { get; set; }
    public required string Alphabet { get; set; }
    public required IArenaTreeRunes<char> Runes { get; set; }

    public string? Name { get; set; }
    public string? Description { get; set; }

    public static Translator From(IAddTranslator<char> command) => new()
    {
        Alphabet = command.Alphabet,
        BaseForwardUrl = command.BaseForwardUrl,
        Runes = command.Runes,

        Name = command.Name ?? string.Empty,
        Description = command.Description ?? string.Empty,
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

    public const string WireUnsafe = "#%[]";

    public static string WireAlphabetOf(string alphabet)
    {
        var sb = new System.Text.StringBuilder();

        foreach (char c in alphabet) if (WireUnsafe.IndexOf(c) < 0) sb.Append(c);

        return sb.ToString();
    }

    public static string DecodeRune(string wire, string wireAlphabet, string alphabet, int runeSize)
    {
        long value = 0;

        foreach (char c in wire)
        {
            int digit = wireAlphabet.IndexOf(c);

            if (digit < 0) throw new Exception($"domain error: wire symbol '{c}' is not in wire alphabet");

            value = value * wireAlphabet.Length + digit;
        }

        int symbols = SymbolCount(alphabet);
        var parts = new string[runeSize];

        for (int i = runeSize - 1; i >= 0; i--)
        {
            parts[i] = SymbolOf(alphabet, (int)(value % symbols));
            value /= symbols;
        }

        return string.Concat(parts);
    }

    public const char Pad = ':';

    public static string DecodeTail(string wire, string wireAlphabet, string alphabet, int runeSize)
    {
        string text = DecodeRune(wire, wireAlphabet, alphabet, runeSize);

        int cut = 0;

        while (cut < runeSize && cut < text.Length && text[text.Length - 1 - cut] == Pad) cut++;

        return text.Substring(0, text.Length - cut);
    }
}
