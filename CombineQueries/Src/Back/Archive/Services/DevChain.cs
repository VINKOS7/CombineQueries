using CombineQueries.Domain.Aggregates.Translator;
using CombineQueries.Domain.Aggregates.Translator.types;

namespace CombineQueries.Api.Services.Speech;

// Одна цепочка, внесённая в дерево руками - только для dev.
//
// В dev connect сбрасывает накопленное, то есть авто-гиперизации между прогонами нет: всё, что
// клиент собрал в прошлый раз, стёрто. Но чтобы персист был ВИДЕН, в дереве должна лежать хотя бы
// одна цепочка, которую клиент в этом прогоне не собирал - её прыжок приезжает сидом в connect,
// и адрес уходит в два запроса с первого раза.
//
// Сеем не вписыванием строк в таблицу, а ПРОИГРЫВАНИЕМ сборки: те же Accept/AcceptVirtualFragment/
// Hop/Close, что зовут хендлеры. Поэтому шаги совпадают с настоящим проходом байт в байт, и живой
// клиент потом попадёт в тот же узел, а не заведёт рядом второй.
public static class DevChain
{
    public static int Seed(ISpeech speech, string url)
    {
        if (speech.Alphabet is null || speech.RuneAlphabet is null || string.IsNullOrEmpty(url)) return -1;

        string alphabet = speech.Alphabet;
        var fragments = speech.FragmentTexts;

        int symbols = speech.SymbolsOf(TypeQuery.Fragmentate);
        int capacity = speech.DfaSize * speech.PageCount;
        int width = WidthOf(symbols, speech.RuneSize, speech.RuneAlphabet.Length);

        var acc = new List<int>();
        int pos = 0;

        while (pos < url.Length)
        {
            // Фрагмент - только на границе руны: ровно так же ищет клиент, самый длинный подходящий.
            if (acc.Count == 0)
            {
                int id = -1, length = 0;

                for (int i = 0; i < fragments.Count; i++)
                {
                    if (fragments[i].Length <= length || pos + fragments[i].Length > url.Length) continue;
                    if (string.CompareOrdinal(url, pos, fragments[i], 0, fragments[i].Length) != 0) continue;

                    id = i;
                    length = fragments[i].Length;
                }

                if (id >= 0)
                {
                    int hop = id / capacity;
                    int anchor = id - hop * capacity;
                    int plain = (length + speech.RuneSize - 1) / speech.RuneSize;

                    if (hop < speech.HopCount && (hop > 0 ? 2 : 1) <= plain)
                    {
                        speech.SetFragmentPage(anchor / speech.DfaSize);
                        speech.AcceptVirtualFragment(anchor % speech.DfaSize);

                        if (hop > 0) speech.Hop(hop);

                        pos += length;
                        continue;
                    }
                }
            }

            int symbol = SymbolAt(url, pos, alphabet, out int taken);

            if (symbol < 0) return -1;

            acc.Add(symbol);
            pos += taken;

            if (acc.Count < speech.RuneSize) continue;

            long value = 0;

            foreach (int index in acc) value = value * symbols + index;

            speech.Accept(RuneOf(value, speech.RuneAlphabet, width));

            acc.Clear();
        }

        // Хвост в цепочку не входит, но сборку закрывает именно он: без Close узел не ляжет в дерево.
        var tail = new System.Text.StringBuilder();

        foreach (int index in acc) tail.Append(Translator.SymbolOf(alphabet, index));

        speech.Close(tail.ToString(), TypeQuery.Fragmentate);

        return speech.LastLeaf;
    }

    // Символ рун-пространства: самый длинный корень L1 либо одиночная буква алфавита.
    private static int SymbolAt(string url, int pos, string alphabet, out int taken)
    {
        int best = -1, length = 0;

        for (int f = 0; f < Translator.Fragments.Length; f++)
        {
            string root = Translator.Fragments[f];

            if (root.Length <= length || pos + root.Length > url.Length) continue;
            if (string.CompareOrdinal(url, pos, root, 0, root.Length) != 0) continue;

            best = alphabet.Length + f;
            length = root.Length;
        }

        if (best >= 0) { taken = length; return best; }

        taken = 1;

        int index = alphabet.IndexOf(url[pos]);

        return index;
    }

    // Значение руны строкой рун-алфавита - обратная сторона Translator.ValueOf.
    private static string RuneOf(long value, string runeAlphabet, int width)
    {
        var runes = new char[width];

        for (int i = width - 1; i >= 0; i--)
        {
            runes[i] = runeAlphabet[(int)(value % runeAlphabet.Length)];
            value /= runeAlphabet.Length;
        }

        return new string(runes);
    }

    // Сколько разрядов рун-алфавита нужно, чтобы вместить symbols^runeSize.
    private static int WidthOf(int symbols, int runeSize, int runeAlphabet)
    {
        long span = 1;

        for (int i = 0; i < runeSize; i++) span *= symbols;

        int width = 1;
        long fits = runeAlphabet;

        while (fits < span) { fits *= runeAlphabet; width++; }

        return width;
    }
}
