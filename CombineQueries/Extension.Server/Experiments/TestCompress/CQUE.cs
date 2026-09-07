using System.Text;

public class CQUE
{
    // Основание паковки пары ФИКСИРОВАНО (не длина алфавита!). Если брать длину алфавита,
    // она растёт по уровням -> compress и decompress используют РАЗНЫЕ основания -> мусор.
    const int BASE = 256;

    // Промежуточные "числа-как-символы" сдвигаем выше базового ASCII, чтобы они НИКОГДА
    // не совпали с символом базового алфавита - иначе условие выхода IsFullyDecoded врёт.
    const int SHIFT = 0x2000;

    // один шаг сжатия: пары символов -> числа. Всегда парами (шаг 2).
    public int[] Compress(string input, string alphabet)
    {
        if (string.IsNullOrEmpty(input) || input.Length % 2 != 0) return new int[0];

        int n = input.Length / 2;
        int[] res = new int[n];

        for (int b = 0; b < n; b++)
        {
            int i1 = alphabet.IndexOf(input[b * 2]);
            int i2 = alphabet.IndexOf(input[b * 2 + 1]);

            if (i1 < 0 || i2 < 0) return new int[0];

            res[b] = i1 * BASE + i2;
        }

        return res;
    }

    // один шаг расжатия: числа -> пары символов. То же основание BASE.
    public string Decompress(int[] input, string alphabet)
    {
        if (input == null || input.Length == 0) return "";

        var sb = new StringBuilder();

        foreach (int id in input)
        {
            sb.Append(alphabet[id / BASE]);
            sb.Append(alphabet[id % BASE]);
        }

        return sb.ToString();
    }

    // Рекурсивно жмёт до <= sizeChein чисел, РАСТИТ общий алфавит (через ref - он нужен
    // декомпрессору, чтобы мапить добавленные символы обратно).
    public int[] CompressRecursive(string input, ref string alphabet, int sizeChein)
    {
        int[] current = Compress(input, alphabet);

        string asString = NumsToString(current);
        alphabet = AppendMissing(alphabet, asString);

        while (current.Length > sizeChein && current.Length % 2 == 0)
        {
            current = Compress(asString, alphabet);

            asString = NumsToString(current);
            alphabet = AppendMissing(alphabet, asString);
        }

        return current;
    }

    // Расжимает, пока результат не станет ЦЕЛИКОМ из базовых символов (первые baseLen).
    // baseLen - длина ИСХОДНОГО алфавита (до роста!), НЕ финального. alphabet - ФИНАЛЬНЫЙ (после роста).
    public string DecompressRecursive(int[] compressed, string alphabet, int baseLen)
    {
        string current = Decompress(compressed, alphabet);

        while (!IsFullyDecoded(current, alphabet, baseLen))
            current = Decompress(ToIntArray(current), alphabet);

        return current;
    }

    // символ из расширенной части (позиция в алфавите >= baseLen) = ещё сжатый уровень, не текст
    static bool IsFullyDecoded(string s, string fullAlphabet, int baseLen)
    {
        foreach (char c in s)
            if (fullAlphabet.IndexOf(c) >= baseLen) return false;

        return true;
    }

    // число -> сдвинутый символ (для хранения в строке/алфавите)
    private static string NumsToString(int[] nums)
    {
        if (nums == null || nums.Length == 0) return "";

        char[] res = new char[nums.Length];
        for (int i = 0; i < nums.Length; i++) res[i] = (char)(nums[i] + SHIFT);

        return new string(res);
    }

    // сдвинутый символ -> исходное число
    private static int[] ToIntArray(string s)
    {
        int[] r = new int[s.Length];
        for (int i = 0; i < s.Length; i++) r[i] = s[i] - SHIFT;

        return r;
    }

    static string AppendMissing(string a, string b)
    {
        var sb = new StringBuilder(a);
        string cur = a;

        foreach (char c in b)
            if (cur.IndexOf(c) < 0) { sb.Append(c); cur = sb.ToString(); }

        return sb.ToString();
    }
}
