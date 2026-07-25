using DomainTranslator = CombineQueries.Domain.Aggregates.Translator.Translator;

// Проверка сценария 2 на НАСТОЯЩЕМ домене (не на копии из Lib.cs):
// в простом режиме дерево кормится через Translator.Learn, а Translator.TreeDensity показывает,
// когда tree-режим станет >=2x плотнее простого - то есть когда клиенту пора переключаться.
static class DomainWarmup
{
    public static void Run(string alphabet, string message, int passes, double switchAt = 2.0)
    {
        Console.WriteLine($"Сообщение: \"{message}\" ({message.Length} симв), {passes} отправок подряд, порог переключения {switchAt:F1}x");
        Console.WriteLine();
        Console.WriteLine("отправка | плотность дерева | режим клиента | узлов");
        Console.WriteLine("---------|------------------|---------------|------");

        var tree = DomainTranslator.ATRFrom(alphabet);

        for (int p = 1; p <= passes; p++)
        {
            // клиент меряет ПЕРЕД отправкой: чем кодировать это сообщение
            double density = DomainTranslator.TreeDensity(message, alphabet, tree);
            bool useTree = density >= switchAt;

            Console.WriteLine($"{p,8} | {density,15:F2}x | {(useTree ? "TREE" : "simple"),13} | {NodeCount(tree),5}");

            // простой режим: дерево ничем не кормится сам по себе - учим вручную (обе стороны).
            // tree-режим: дерево растёт внутри декода, отдельный Learn НЕ нужен.
            if (!useTree) DomainTranslator.Learn(message, tree);
            else DomainTranslator.Learn(message, tree); // в этом стенде нет реального декода, эмулируем рост
        }

        Console.WriteLine();
    }

    static int NodeCount(CombineQueries.Domain.Aggregates.Translator.types.IArenaTreeRunes<char> tree)
    {
        int n = 0;
        try { while (true) { tree.Get(n); n++; } } catch { }
        return n;
    }
}
