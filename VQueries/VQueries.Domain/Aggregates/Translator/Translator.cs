using Dotseed.Domain;
using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Domain.Aggregates.Translator;

internal static class IntExtns
{
    public static string ArrayToString(this int[] array)
    {
        if (array == null || array.Length == 0) return "";

        char[] res = new char[array.Length];

        for (int i = 0; i < array.Length; i++) res[i] = (char)array[i];

        return new string(res);
    }
}

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

        foreach (char c in alphabet) arena.From(new ArenaTreeRunes<char>().Root, c);

        return arena;
    }


    public string Unruned(string runes)
    {
        int id = 0;

        foreach (var c in runes)
        {
            int digit = Alphabet.IndexOf(c);

            if (digit < 0) throw new Exception($"domain error: symbol '{c}' not in alphabet");

            id = id * Alphabet.Length + digit;
        }

        var chars = new Stack<char>();
        var node = Runes.Get(id);

        while (node.ParentId != -1)
        {
            chars.Push(node.ParentSymbol);

            node = Runes.Get(node.ParentId);
        }

        return new string(chars.ToArray());
    }
}