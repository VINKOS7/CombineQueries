using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

public class CombineQueriesTest : UdonSharpBehaviour
{
    public CombineQueries client;

    [Tooltip("0 = Init, 1 = run the three-step comparison on testUrl")]
    public int action = 0;

    [Tooltip("One url for all three steps: full send, then its hyper, then full send without the dictionary")]
    public string testUrl = "https://dummyjson.com/todos/1";

    [Tooltip("Optional: status is written here")]
    public Text output;

    private const int StepFull = 0;
    private const int StepHyper = 1;
    private const int StepPlain = 2;

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

        if (action == 0) { client.Init(); awaiting = true; Say("init sent"); return; }

        if (!ready) { Say("run Init first"); return; }

        if (running) { running = false; Say("run stopped"); return; }

        running = true;
        step = StepFull;
        board = testUrl + "   " + NumberOf(testUrl.Length) + " chars\n\n";

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
                    + NumberOf(client.LastSymbols) + " symbols";

        board += line + "\n";
        step++;

        Note(line);
        Show("");

        if (step <= StepPlain) { SendStep(); return; }

        running = false;

        Note("done");
        Show("\n" + client.TakeForwardedBody());
    }

    private void SendStep()
    {
        if (step == StepPlain) client.RequestDirect(testUrl);
        else client.Request(testUrl);

        awaiting = true;
        startedAt = Time.time;

        Note(TitleOf(step) + "   sending " + client.LastUrl);
        Show(TitleOf(step) + "   sending...");
    }

    private string TitleOf(int at)
    {
        if (at == StepFull) return "1  full send, fragment symbols   ";
        if (at == StepHyper) return "2  hyper, the server knew it     ";

        return "3  full send, direct symbols only";
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
