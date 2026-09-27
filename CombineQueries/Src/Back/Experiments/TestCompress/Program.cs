class Program
{
    static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var cque = new CQUE();
        string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789-._~:/?#[]@!$&'()*+,;=%";

        Test(cque, alphabet, "12345678");
        Test(cque, alphabet, "navicororobawsvi");
        Test(cque, alphabet, "aaaaaaaa");
        Test(cque, alphabet, "abcdefghabcdefgh");
    }

    static void Test(CQUE cque, string baseAlphabet, string input)
    {
        string workAlphabet = baseAlphabet;
        int[] alf = cque.CompressRecursive(baseAlphabet, ref workAlphabet, 2);

        // клиент: растит рабочую копию алфавита (ref)
        int[] encoded = cque.CompressRecursive(input, ref baseAlphabet, 2);

        // сервер: ФИНАЛЬНЫЙ алфавит + длина БАЗОВОГО как порог выхода
        // workAlphabet не нужно брать с клиента, достаточно вычислять
        string decoded = cque.DecompressRecursive(encoded, workAlphabet, baseAlphabet.Length);

        bool ok = decoded == input;

        Console.WriteLine($"input     ({input.Length,2}): \"{input}\"");
        Console.WriteLine($"encoded   ({encoded.Length,2}): {string.Join(",", encoded)}");
        Console.WriteLine($"decoded   ({decoded.Length,2}): \"{decoded}\"");
        Console.WriteLine($"roundtrip: {(ok ? "OK" : "!!! MISMATCH")}");
        Console.WriteLine();
    }
}
