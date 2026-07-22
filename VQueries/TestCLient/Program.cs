class Program
{
    static void Main(string[] args)
    {
        var cque = new CQUE();
        string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789-._~:/?#[]@!$&'()*+,;=%";

        // Входная строка клиента (8 символов)
        string input = "navicororobawsvi";
        string imput2 = "navicororobawsvi";


        // Кодируем: на выходе получаем массив чисел (ровно 4 элемента)
        int[] encoded = cque.Compress(input, alphabet);

        //клиент
        var runes21 = cque.Compress(imput2, alphabet);

        var r21 = runes21.Take(8);
        var r22 = runes21.Skip(8).Take(8);

        int[] encoded2 = cque.Compress(IntArrayToString(runes21), alphabet + IntArrayToString(runes21));
        //server


        // Декодируем обратно в строку на сервере
        string decoded = cque.Decompress(encoded, alphabet);

        string decoded2 = cque.Decompress(cque.Decompress(encoded2, alphabet + IntArrayToString(runes21)).Select(v => (int) v).ToArray(), alphabet);


        Console.WriteLine($"Original ({input.Length} chrs): {input}");
        Console.WriteLine($"Original2 ({imput2.Length} chrs): {imput2}");


        Console.WriteLine($"Compressed IDs ({encoded.Length} items): {IntArrayToString(encoded)}");
        Console.WriteLine($"Compressed 2 IDs ({encoded2.Length} items): {IntArrayToString(encoded2)}");

        Console.WriteLine($"Decompress ({decoded.Length} chrs): {decoded}");
        Console.WriteLine($"Decompress 2 ({decoded2.Length} chrs): {decoded2}");

        Console.WriteLine();
        Console.WriteLine("=== то же самое через цикл (CompressRecursive/DecompressRecursive) ===");

        int[] encoded3 = cque.CompressRecursive(imput2, alphabet, 4, out var alphabetLevels);
        string decoded3 = cque.DecompressRecursive(encoded3, alphabetLevels);

        Console.WriteLine($"Compressed 3 IDs ({encoded3.Length} items): {IntArrayToString(encoded3)}, levels={alphabetLevels.Count}");
        Console.WriteLine($"Decompress 3 ({decoded3.Length} chrs): {decoded3}");

        Console.ReadLine();
    }

    public static string IntArrayToString(int[] array)
    {
        if (array == null || array.Length == 0) return "";

        char[] res = new char[array.Length];

        for (int i = 0; i < array.Length; i++) res[i] = (char) array[i];
        return new string(res);
    }

    public static string IntArrayToStringByAlph(int[] array, string alphabet)
    {
        if (array == null || array.Length == 0 || string.IsNullOrEmpty(alphabet)) return "";

        char[] res = new char[array.Length];

        for (int i = 0; i < array.Length; i++)
        {
            if (array[i] < 0 || array[i] >= alphabet.Length) return "";

            res[i] = alphabet[array[i]];
        }

        return new string(res);
    }
}