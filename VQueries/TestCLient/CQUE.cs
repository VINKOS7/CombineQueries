public class CQUE
{
    // СЖАТИЕ: Из 8 символов делает массив из 4 чисел (длина ровно в 2 раза меньше)
    public int[] Compress(string input, string clientAlphabet)
    {
        if (string.IsNullOrEmpty(input) || input.Length % 2 != 0 || string.IsNullOrEmpty(clientAlphabet)) return new int[0];

        int baseLen = clientAlphabet.Length;
        int totalBlocks = input.Length / 2;
        int[] res = new int[totalBlocks];

        for (int b = 0; b < totalBlocks; b++)
        {
            int idx1 = clientAlphabet.IndexOf(input[b * 2]);
            int idx2 = clientAlphabet.IndexOf(input[b * 2 + 1]);
            if (idx1 < 0 || idx2 < 0) return [0];

            // Формула плоской матрицы: переводим пару в один уникальный ID
            res[b] = (idx1 * baseLen) + idx2;
        }
        return res;
    }

    // РАСЖАТИЕ: Из массива чисел восстанавливает исходную строку текста
    public string Decompress(int[] input, string clientAlphabet)
    {
        if (input == null || input.Length == 0 || string.IsNullOrEmpty(clientAlphabet)) return "";

        int baseLen = clientAlphabet.Length;
        char[] res = new char[input.Length * 2];
        int resIdx = 0;

        for (int b = 0; b < input.Length; b++)
        {
            int id = input[b];

            // Обратная математика матрицы: достаем координаты X и Y из ID
            int idx1 = id / baseLen;
            int idx2 = id % baseLen;

            res[resIdx] = clientAlphabet[idx1];
            res[resIdx + 1] = clientAlphabet[idx2];
            resIdx += 2;
        }
        return new string(res);
    }

    public int[] CompressRecursive(string input, string alphabet, int sizeChein, out List<string> alphabetLevels)
    {
        alphabetLevels = new List<string> { alphabet };

        string currentAlphabet = alphabet;
        int[] current = Compress(input, currentAlphabet);

        while (current.Length > sizeChein && current.Length % 2 == 0 && current.Length > 0)
        {
            string asString = IntArrayToString(current);

            currentAlphabet = currentAlphabet + asString;
            alphabetLevels.Add(currentAlphabet);

            current = Compress(asString, currentAlphabet);
        }

        return current;
    }

    public string DecompressRecursive(int[] compressed, List<string> alphabetLevels)
    {
        int[] current = compressed;

        for (int level = alphabetLevels.Count - 1; level >= 1; level--)
        {
            string decoded = Decompress(current, alphabetLevels[level]);

            current = new int[decoded.Length];

            for (int i = 0; i < decoded.Length; i++) current[i] = decoded[i];
        }

        return Decompress(current, alphabetLevels[0]);
    }

    private static string IntArrayToString(int[] array)
    {
        if (array == null || array.Length == 0) return "";

        char[] res = new char[array.Length];
        for (int i = 0; i < array.Length; i++) res[i] = (char)array[i];
        return new string(res);
    }
}
