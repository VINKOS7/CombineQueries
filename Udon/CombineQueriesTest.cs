using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

public class CombineQueriesTest : UdonSharpBehaviour
{
    public CombineQueries client;

    [Tooltip("0 = Init, 1 = request a single url, 2 = cycle through a list")]
    public int action = 0;

    [Tooltip("What to request when action = 1")]
    public string testUrl = "http://example.com/";

    [Header("Cycling run (action = 2)")]

    [Tooltip("A number is appended to it: .../todos/1, .../todos/2, ...")]
    public string cycleBaseUrl = "https://dummyjson.com/todos/";

    [Tooltip("How many different urls in one lap")]
    public int cycleCount = 3;

    [Tooltip("Extra seconds between requests. 0 = platform cooldown only")]
    public float cyclePeriod = 0f;

    [Tooltip("Optional: status is written here")]
    public Text output;

    private bool ready;
    private bool awaiting;
    private bool cycling;
    private int cycleIndex;
    private int lap;
    private float nextSendAt;
    private float startedAt;
    private string lastResult = "";

    public override void Interact()
    {
        if (client == null) { Log("client is not assigned"); return; }
        if (awaiting) return;

        if (action == 0) { client.Init(); awaiting = true; Log("init sent"); return; }

        if (action == 1) { Send(testUrl); return; }

        if (cycling) { cycling = false; Log("cycle stopped"); return; }

        if (!ready) { Log("run Init first"); return; }

        cycling = true;
        cycleIndex = 0;
        lap = 1;
        nextSendAt = Time.time;

        Log("cycle started: " + NumberOf(cycleCount) + " urls");
    }

    public void OnQueryDone()
    {
        awaiting = false;

        if (client.LastError != "")
        {
            cycling = false;

            Log("ERROR\n" + client.LastError);
            return;
        }

        if (!ready) { ready = true; Log("ready - touch the green cube to start the demo"); return; }

        lastResult = "lap " + NumberOf(lap) + "   url " + NumberOf(cycleIndex == 0 ? cycleCount : cycleIndex)
                   + "   " + NumberOf((int)((Time.time - startedAt) * 1000f)) + " ms\n\n"
                   + client.TakeForwardedBody();

        nextSendAt = Time.time + cyclePeriod;

        Log(lastResult);
    }

    void Update()
    {
        if (!cycling || awaiting || Time.time < nextSendAt) return;

        Send(cycleBaseUrl + NumberOf(cycleIndex + 1));

        cycleIndex++;

        if (cycleIndex < cycleCount) return;

        cycleIndex = 0;
        lap++;
    }

    private void Send(string url)
    {
        if (!ready) { Log("run Init first"); return; }

        client.Request(url);

        awaiting = true;
        startedAt = Time.time;

        Log("sending " + url + "\n\n" + lastResult);
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

    private void Log(string message)
    {
        Debug.Log("[CombineQueriesTest] " + message);

        if (output != null) output.text = message;
    }
}
