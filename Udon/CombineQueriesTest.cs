using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

public class CombineQueriesTest : UdonSharpBehaviour
{
    public CombineQueries client;

    [Tooltip("0 = Init, 1 = run the comparison, 2 = Remember (re-auth only)")]
    public int action = 0;

    [Tooltip("Codeword the server expects (Auth:Codeword); empty in dev")]
    public string codeword = "";

    // code-only, not serialized - a scene value cannot override these and desync the labels
    private string testUrlFull = "https://dummyjson.com/comments/1";
    private string testUrl = "https://dummyjson.com/products/1";

    [Tooltip("Optional: status is written here")]
    public Text output;

    private const int StepCombineFull = 0;
    private const int StepCombinePartial = 1;
    private const int StepDirectFull = 2;
    private const int StepDirectPartial = 3;

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

        if (action == 0) { client.codeword = codeword; client.Init(); awaiting = true; Say("init sent"); return; }

        if (action == 2) { client.codeword = codeword; client.Remember(); awaiting = true; Say("remember sent"); return; }

        if (!ready) { Say("run Init first"); return; }

        if (running) { running = false; Say("run stopped"); return; }

        running = true;
        step = StepCombineFull;
        board = testUrlFull + "   " + NumberOf(testUrlFull.Length) + " chars   (full - /comments/)\n"
              + testUrl + "   " + NumberOf(testUrl.Length) + " chars   (partial - /products/)\n\n";

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

        if (step <= StepDirectPartial) { SendStep(); return; }

        running = false;

        Note("done");
    }

    private void SendStep()
    {
        if (step == StepCombineFull) client.Request(testUrlFull);
        else if (step == StepCombinePartial) client.Request(testUrl);
        else if (step == StepDirectFull) client.RequestDirect(testUrlFull);
        else client.RequestDirect(testUrl);

        awaiting = true;
        startedAt = Time.time;

        Note(TitleOf(step) + "   sending " + client.LastUrl);
        Show(TitleOf(step) + "   sending...");
    }

    private string TitleOf(int at)
    {
        if (at == StepCombineFull) return "1  combine, full-fragments (/comments/)";
        if (at == StepCombinePartial) return "2  combine, partial        (/products/)";
        if (at == StepDirectFull) return "3  direct                  (/comments/)";

        return "4  direct                  (/products/)";
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
