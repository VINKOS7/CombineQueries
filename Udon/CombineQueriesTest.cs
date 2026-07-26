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

    // Two periods, and that is the whole point of the demo: the first lap sends every url in
    // full and crawls, every later lap goes through a handle and flies. The speed difference
    // is the protocol becoming visible - nobody reads a log, but everybody notices the pace.
    //
    // Both are floors, not guarantees: a send still in flight pushes the next one out.
    [Tooltip("Minimum seconds between sends while urls are still unknown to the server")]
    public float cyclePeriod = 3f;

    [Tooltip("Minimum seconds between sends once the url is cached - deliberately much shorter")]
    public float cachedPeriod = 0.5f;

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

        // The body the target actually answered, not the server's envelope around it -
        // for the todos demo that is the line of json which changes every send.
        string body = client.TakeForwardedBody();

        if (!cycling) { Log("done\n" + body); return; }

        // The next delay is chosen by how this send actually travelled, not by the lap number:
        // a cached url speeds the cycle up the moment it becomes cached.
        bool cached = client.LastSendWasCached();

        nextSendAt = Time.time + (cached ? cachedPeriod : cyclePeriod);

        int ms = client.LastSendMs();

        // Remember the full send so later laps can be compared against it, not against nothing
        if (!cached) fullSendMs = ms;

        Log(Explain(cached, client.LastRequestCount(), ms) + "\n\n" + body);
    }

    private int fullSendMs = -1;

    // The status board. Written for someone standing in the world who has never seen this
    // project: it has to say what just happened and why the pace changed.
    private string Explain(bool cached, int requests, int ms)
    {
        string head = "lap " + NumberOf(lap) + "   url " + NumberOf(cycleIndex == 0 ? cycleCount : cycleIndex)
                    + "/" + NumberOf(cycleCount) + "\n";

        if (cached)
        {
            // Against the full send of the SAME demo, so the number means something on the spot
            string versus = fullSendMs > 0 ? "   (full send took " + NumberOf(fullSendMs) + " ms)" : "";

            return head
                 + "CACHED - 1 request - " + NumberOf(ms) + " ms" + versus + "\n"
                 + "The server already knows this url and kept a short handle for it,\n"
                 + "so the whole url fits into that one request. This is why it sped up.";
        }

        return head
             + "FULL SEND - " + NumberOf(requests) + " requests - " + NumberOf(ms) + " ms\n"
             + "VRChat can only load urls baked in at build time, so an arbitrary url is\n"
             + "spelled out a couple of characters per request. The server reassembles it,\n"
             + "forwards it, and hands back a handle - watch the next lap.";
    }

    private string lastSeen = "";

    void Update()
    {
        if (client == null) return;

        Tick();

        // Update only keeps what does not arrive as an event - the initialization status
        string now;

        if (client.LastError != "") now = "ERROR\n" + client.LastError;
        else if (!client.IsInitialized()) now = "not initialized - touch the blue cube";
        else if (client.IsBusy()) return;      // OnQueryDone owns the board while a send is running
        else if (cycling) return;              // ...and between sends too
        else now = "ready - touch the green cube to start the demo\ninit: " + client.InitInfo;

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

        Log("lap " + NumberOf(lap) + "   sending " + url + "\n...");

        // A pessimistic floor until the send lands. OnQueryDone replaces it with the real one,
        // which depends on whether this url turned out to be cached.
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
