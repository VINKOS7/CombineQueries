class Program
{
    static void Main(string[] args)
    {
        var cque = new CQUE();
        string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789-._~:/?#[]@!$&'()*+,;=%\"";

        string input = "aswkafsghjfsagvsefvzjvfhjfgzefvmghcsfghmvSHCMvfcSGhzzshgdfvcsdgh".ToLower();

        if (input.Length % 2 is not 0) input = input + '"';

        int[] encoded = cque.CompressRecursive(input, alphabet, 2, out var alphabetLevels);//client 

        string decoded = cque.DecompressRecursive(encoded, alphabetLevels);//server

        Console.WriteLine($"Compressed counts wires ({encoded.Length} items): {IntArrayToString(encoded)}, levels={alphabetLevels.Count}");
        Console.WriteLine($"Decompress undon input ({decoded.Length} chrs): {decoded}");

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