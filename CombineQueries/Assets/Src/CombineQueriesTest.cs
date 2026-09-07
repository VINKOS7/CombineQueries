using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

public class CombineQueriesTest : UdonSharpBehaviour
{
    public CombineQueries client;

    [Tooltip("0 = Connect, 1 = run the comparison, 2 = Remember (повторное подключение дельтой)")]
    public int action = 0;

    [Tooltip("Codeword the server expects (Auth:Codeword); empty in dev")]
    public string codeword = "";

    // code-only, not serialized - a scene value cannot override these and desync the labels
    //
    // Порядок демонстрации: сперва уровни словаря, потом хайпер, потом бесконечность двумя
    // заходами, и только в конце частичное покрытие с прямой отправкой - они самые дорогие.
    private string testUrlFull = "https://dummyjson.com/comments/1";

    // Бесконечность показываем ДВУМЯ РАЗНЫМИ url с общей длинной частью. Повтор одного и того же
    // дал бы хайпер (клиент помнит url -> handle) и ничего про Infinite не сказал бы: на первом
    // заходе сервер учит общую подстроку, на втором она уже адресуется.
    private string testUrlLearn = "https://dummyjson.com/products?limit=10&skip=20";
    private string testUrlLearned = "https://dummyjson.com/products?limit=10&skip=50";

    private string testUrl = "https://dummyjson.com/comments/post/1";

    [Tooltip("Optional: status is written here")]
    public Text output;

    private const int StepLevels = 0;
    private const int StepHyper = 1;
    private const int StepLearn = 2;
    private const int StepLearned = 3;
    private const int StepCombinePartial = 4;
    private const int StepDirect = 5;

    private bool ready;
    private bool awaiting;
    private bool running;
    private int step;
    private float startedAt;
    private string board = "";

    public override void Interact()
    {
        if (client == null) { Say("client is not assigned"); return; }
        if (awaiting) return;

        if (action == 0)
        {
            client.codeword = codeword;
            client.Connect();

            // Раньше здесь стояло безусловное «init sent», даже когда метод выходил молча
            // (busy или отказ) - и это выглядело как зависание. Теперь говорим, что произошло.
            awaiting = client.LastError == "";

            Say(awaiting ? "connect sent" : "connect refused: " + client.LastError);
            return;
        }

        if (action == 2)
        {
            client.codeword = codeword;
            client.Remember();

            awaiting = client.LastError == "";

            Say(awaiting ? "remember sent" : "remember refused: " + client.LastError);
            return;
        }

        if (!ready) { Say("run Connect first"); return; }

        if (running) { running = false; Say("run stopped"); return; }

        running = true;
        step = StepLevels;
        board = testUrlFull + "   " + NumberOf(testUrlFull.Length) + " chars   (levels L1-L3)\n"
              + testUrlLearn + "   " + NumberOf(testUrlLearn.Length) + " chars   (infinite: learn, then reuse)\n"
              + testUrl + "   " + NumberOf(testUrl.Length) + " chars   (partial - post/1 is plain)\n\n";

        SendStep();
    }

    public void OnQueryDone()
    {
        awaiting = false;

        if (client.LastError != "")
        {
            running = false;

            Say("ERROR\n" + client.LastError);
            return;
        }

        if (!ready) { ready = true; Say("ready - touch the green cube"); return; }
        if (!running) return;

        string line = TitleOf(step) + "   " + NumberOf((int)((Time.time - startedAt) * 1000f)) + " ms   "
                    + NumberOf(client.LastQueries) + " queries";

        board += line + "\n";
        step++;

        Note(line);
        Show("\n" + client.TakeForwardedBody());

        if (step <= StepDirect) { SendStep(); return; }

        running = false;

        Note("done");
    }

    private void SendStep()
    {
        if (step == StepLevels) client.Request(testUrlFull);
        else if (step == StepHyper) client.Request(testUrlFull);
        else if (step == StepLearn) client.Request(testUrlLearn);
        else if (step == StepLearned) client.Request(testUrlLearned);
        else if (step == StepCombinePartial) client.Request(testUrl);
        else client.RequestDirect(testUrl);

        awaiting = true;
        startedAt = Time.time;

        Note(TitleOf(step) + "   sending " + client.LastUrl);
        Show(TitleOf(step) + "   sending...");
    }

    private string TitleOf(int at)
    {
        if (at == StepLevels) return "1  L1-L3 fragments        (comments/1)       ";
        if (at == StepHyper) return "2  hyper (cached handle)  (comments/1)       ";
        if (at == StepLearn) return "3  infinite, learning     (limit=10&skip=20) ";
        if (at == StepLearned) return "4  infinite, learned      (limit=10&skip=50) ";
        if (at == StepCombinePartial) return "5  combine, partial       (comments/post/1)";

        return "6  direct                 (comments/post/1)";
    }

    private string NumberOf(int value)
    {
        if (value <= 0) return "0";

        string digits = "";

        while (value > 0)
        {
            digits = "0123456789".Substring(value % 10, 1) + digits;
            value /= 10;
        }

        return digits;
    }

    private void Note(string line) => Debug.Log("[CombineQueriesTest] " + line);

    private void Show(string tail)
    {
        if (output != null) output.text = board + tail;
    }

    private void Say(string message)
    {
        Note(message);
        Show(message);
    }
}
