using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

// Test driver. Put it on a clickable object (a collider is required).
// Clicking runs whatever `action` selects. Output goes to Debug.Log and, if assigned, to a Text.
public class CombineQueriesTest : UdonSharpBehaviour
{
    public CombineQueries client;

    [Tooltip("0 = Init, 1 = send a single url, 2 = cycle through a list")]
    public int action = 0;

    [Tooltip("What to send when action = 1")]
    public string testUrl = "http://example.com/";

    [Header("Cycling run (action = 2)")]

    // Shared part of the url. Editable in the inspector so it can point at your own server.
    //
    // dummyjson rather than jsonplaceholder: 29 characters against 44, and length here IS the
    // request count (length / RuneSize). At RuneSize = 2 that is 16 requests instead of 23 on
    // every first send. Returns ~90 bytes of JSON, ids 1..150 are live.
    [Tooltip("A number is appended to it: .../todos/1, .../todos/2, ...")]
    public string cycleBaseUrl = "https://dummyjson.com/todos/";

    [Tooltip("How many different urls in one lap")]
    public int cycleCount = 3;

    [Tooltip("Seconds between sends")]
    public float cyclePeriod = 2f;

    [Tooltip("Optional: status is written here")]
    public Text output;

    // --- cycle state ---

    private bool cycling;
    private int cycleIndex;     // which url goes next: 0..cycleCount-1
    private int lap;            // lap number, from 1. The second lap is where /h kicks in
    private float nextSendAt;

    public override void Interact()
    {
        if (client == null) { Log("client is not assigned"); return; }

        if (action == 0)
        {
            client.Init();
            Log("Init sent");

            return;
        }

        if (action == 1)
        {
            client.Send(testUrl);
            Log("Send sent: " + testUrl);

            return;
        }

        // Clicking the same object again stops the run - otherwise there is no way to turn it
        // off and it keeps hammering requests forever.
        if (cycling)
        {
            cycling = false;
            Log("cycle stopped");

            return;
        }

        if (!client.IsInitialized()) { Log("run Init first"); return; }

        cycling = true;
        cycleIndex = 0;
        lap = 1;
        nextSendAt = Time.time;      // first send goes immediately, without waiting a period

        Log("cycle started: " + NumberOf(cycleCount) + " urls, one every " + cyclePeriod + "s");
    }

    // The client calls this itself on completion (SendCustomEvent), no polling needed.
    // The name must match onDoneEvent in the CombineQueries inspector.
    public void OnQueryDone()
    {
        if (client.LastError != "") { Log("ERROR\n" + client.LastError); return; }

        Log((cycling ? "lap " + NumberOf(lap) + "\n" : "done\n") + client.TakeResult());
    }

    private string lastSeen = "";

    void Update()
    {
        if (client == null) return;

        Tick();

        // Update only keeps what does not arrive as an event - the initialization status
        string now;

        if (client.LastError != "") now = "ERROR\n" + client.LastError;
        else if (!client.IsInitialized()) now = "not initialized - press Init";
        else if (client.IsBusy()) now = "sending...";
        else if (cycling) now = "cycle: lap " + NumberOf(lap) + ", next " + NumberOf(cycleIndex + 1);
        else now = "init: " + client.InitInfo;

        if (now == lastSeen) return;

        lastSeen = now;

        Log(now);
    }

    private void Tick()
    {
        if (!cycling) return;

        // Do not start a new send while the previous one is in flight. The client has a single
        // send buffer, not a message queue, so the chunk sequence would be overwritten midway.
        if (client.IsBusy()) return;

        if (Time.time < nextSendAt) return;

        // The urls repeat in a loop, so from the SECOND lap on each of them is already interned
        // by the server and travels as one /h request instead of the whole /n + chunks chain.
        // That comparison is the point of this run: the server log shows
        // "1 request, N ms vs first send M ms" side by side.
        string url = cycleBaseUrl + NumberOf(cycleIndex + 1);

        client.Send(url);

        Log("lap " + NumberOf(lap) + ": " + url);

        nextSendAt = Time.time + cyclePeriod;

        cycleIndex++;

        if (cycleIndex < cycleCount) return;

        cycleIndex = 0;
        lap++;
    }

    // int.ToString() carries the same risk class in Udon as string+int concatenation
    // (which has already broken InitQuery here), so the number is built character by character.
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

    private void Log(string msg)
    {
        Debug.Log("[CombineQueriesTest] " + msg);

        if (output != null) output.text = msg;
    }
}
