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

public record CompressedResult(int[] runes, IReadOnlyList<string> alphDmension);

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

    public static string UnrunedCombine(string alphabet, IArenaTreeRunes<char> runes, string wireRunes)
    {
        int wireValue = 0;

        foreach (var c in wireRunes)
        {
            int digit = alphabet.IndexOf(c);

            if (digit < 0) throw new Exception($"domain error: symbol '{c}' is not in alphabet");

            wireValue = wireValue * alphabet.Length + digit;
        }

        int symbolSpan = alphabet.Length + 1;
        var node = runes.Get(wireValue / symbolSpan);
        int symbolIndex = wireValue % symbolSpan;

        var chars = new Stack<char>();
        var walk = node;

        while (walk.ParentId != -1)
        {
            chars.Push(walk.ParentSymbol!);
            walk = runes.Get(walk.ParentId);
        }

        if (symbolIndex < alphabet.Length) runes.From(node, alphabet[symbolIndex]);

        return new string(chars.ToArray());
    }

    public static string UnrunedCombineMany(string alphabet, IArenaTreeRunes<char> runes, IEnumerable<string> wireFragments)
    {
        string result = "";

        foreach (var fragment in wireFragments) result += UnrunedCombine(alphabet, runes, fragment);

        return result;
    }

    public string Unrune(string runes) => UnrunedCombine(Alphabet, Runes, runes);

    // БАЗОВАЯ УЖИМКА. Алфавит расширяется частыми кусками URL, и "символом" становится не буква,
    // а фрагмент. Пара символов по-прежнему схлопывается в один индекс пула - механика та же, что
    // и была, но платит она теперь сразу.
    //
    // Ключевое отличие от прежних alphExts: таблица СТАТИЧЕСКАЯ. Она одинакова у клиента и сервера
    // и никогда не едет по проводу, поэтому синхронизировать нечего - а именно на синхронизации
    // растущих уровней прежняя схема и разваливалась. Цена: пул растёт с 59^2 до (59+N)^2.
    //
    // ПОРЯДОК ВАЖЕН: индекс символа = его позиция здесь. Добавлять только В КОНЕЦ, иначе клиент
    // и сервер разъедутся молча, как это уже было с лишней кавычкой в WireAlphabet.
    public static readonly string[] Fragments =
    [
        "https://", "http://", "www.", ".com", ".org", ".net", ".ru", ".io", ".dev",
        "/api/", "/v1/", "/r/", "/comments/", ".html", ".php", ".json",
        "json", "html", "index", "search", "image", "video", "data", "list", "item",
        "page", "user", "admin", "name", "true", "false", "?id=", "&id=", "://", "com"
    ];

    public static int SymbolCount(string alphabet) => alphabet.Length + Fragments.Length;

    // Индекс -> текст. Первые alphabet.Length индексов это одиночные буквы, дальше фрагменты.
    public static string SymbolOf(string alphabet, int index) =>
        index < alphabet.Length ? alphabet[index].ToString() : Fragments[index - alphabet.Length];

    // Символы, которыми нельзя писать САМ запрос:
    //   # начинает фрагмент, % начинает percent-encoding, [ ] зарезервированы под IPv6-литералы.
    // / и ? остаются - в query-строке они легальны (в пути нет, поэтому руны и едут в query).
    // Выводится детерминированно из исходного алфавита, передавать отдельно не нужно.
    public const string WireUnsafe = "#%[]";

    public static string WireAlphabetOf(string alphabet)
    {
        var sb = new System.Text.StringBuilder();

        foreach (char c in alphabet) if (WireUnsafe.IndexOf(c) < 0) sb.Append(c);

        return sb.ToString();
    }

    // Один кусок: wire-разряды -> значение -> runeSize СИМВОЛОВ -> их текст.
    // Символ это буква или фрагмент из Fragments, поэтому длина результата в символах фиксирована,
    // а в ЗНАКАХ - нет: один кусок может нести и два знака, и шестнадцать.
    public static string DecodeChunk(string wire, string wireAlphabet, string alphabet, int runeSize)
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

    // Растит дерево из ГОТОВОГО текста тем же правилом, что и энкодер: жадно матчим самый длинный
    // известный префикс, добавляем ровно одно расширение, сдвигаемся на длину матча.
    // Нужно для простого режима: там дерево ничем не кормится (руны не адресуют узлы), и без этого
    // оно никогда не прогреется. КРИТИЧНО: клиент и сервер обязаны звать это на ОДНОМ И ТОМ ЖЕ
    // тексте, иначе деревья разъедутся и tree-режим начнёт врать.
    public static void Learn(string text, IArenaTreeRunes<char> runes)
    {
        if (string.IsNullOrEmpty(text)) return;

        int pos = 0;

        while (pos < text.Length)
        {
            var node = runes.Root;
            int depth = 0;

            while (pos + depth < text.Length && node.Next.TryGetValue(text[pos + depth], out int childId))
            {
                node = runes.Get(childId);
                depth++;
            }

            if (pos + depth < text.Length) runes.From(node, text[pos + depth]);

            // depth==0 бывает только если символа нет даже в преседе - иначе зациклимся
            pos += depth == 0 ? 1 : depth;
        }
    }

    // Во сколько раз tree-режим плотнее простого для этого текста при текущем дереве.
    // Простой режим - всегда 1 исходный символ на 1 wire-символ, поэтому знаменатель = text.Length.
    // Считает БЕЗ роста дерева (чистая оценка, дерево не трогает).
    public static double TreeDensity(string text, string alphabet, IArenaTreeRunes<char> runes)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        int symbolSpan = alphabet.Length + 1;
        int wireChars = 0;
        int pos = 0;

        while (pos < text.Length)
        {
            var node = runes.Root;
            int depth = 0;

            while (pos + depth < text.Length && node.Next.TryGetValue(text[pos + depth], out int childId))
            {
                node = runes.Get(childId);
                depth++;
            }

            long wireValue = (long)node.Id * symbolSpan + (pos + depth < text.Length ? 0 : alphabet.Length);

            // сколько base-alphabet разрядов нужно, чтобы записать wireValue
            int digits = 1;
            for (long v = wireValue; v >= alphabet.Length; v /= alphabet.Length) digits++;

            wireChars += digits;
            pos += depth == 0 ? 1 : depth;
        }

        return wireChars == 0 ? 0 : (double)text.Length / wireChars;
    }

    public static int[] Rune(string s, string alphabet)
    {
        if (string.IsNullOrEmpty(s) || s.Length % 2 != 0 || string.IsNullOrEmpty(alphabet)) return [];
        int L = alphabet.Length, n = s.Length / 2;
        var r = new int[n];
        for (int b = 0; b < n; b++)
        {
            int i1 = alphabet.IndexOf(s[b * 2]), i2 = alphabet.IndexOf(s[b * 2 + 1]);
            if (i1 < 0 || i2 < 0) return [];
            r[b] = i1 * L + i2;
        }
        return r;
    }

    public static string Derune(int[] ids, string a)
    {
        if (ids is null || ids.Length == 0 || string.IsNullOrEmpty(a)) return "";
        int L = a.Length;
        var c = new char[ids.Length * 2];
        for (int b = 0; b < ids.Length; b++) { c[b * 2] = a[ids[b] / L]; c[b * 2 + 1] = a[ids[b] % L]; }
        return new string(c);
    }


    public static CompressedResult CompressRecursive(string s, string alphabet, int target) => CompressRecursive(s, alphabet, target, new List<string> { alphabet });

    public static CompressedResult CompressRecursive(string s, string alphabet, int target, List<string> levels)
    {
        int[] cur = Rune(s, alphabet);

        if (cur.Length <= target || cur.Length % 2 != 0 || cur.Length == 0)
            return new CompressedResult(cur, levels);

        string str = new(cur.Select(id => (char)id).ToArray());
        string nextAlphabet = alphabet + str;

        levels.Add(nextAlphabet);

        return CompressRecursive(str, nextAlphabet, target, levels); // самовызов
    }

    public static string Derune(CompressedResult r) => Derune(r.runes, r.alphDmension, r.alphDmension.Count - 1);

    private static string Derune(int[] ids, IReadOnlyList<string> levels, int level)
    {
        if (level == 0) return Derune(ids, levels[0]);

        int[] next = Derune(ids, levels[level]).Select(c => (int)c).ToArray();

        return Derune(next, levels, level - 1); // самовызов
    }
}