using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

public class CombineQueriesTest : UdonSharpBehaviour
{
    public CombineQueries client;

    [Tooltip("0 = Init, 1 = send a single url, 2 = cycle through a list")]
    public int action = 0;

    [Tooltip("What to send when action = 1")]
    public string testUrl = "http://example.com/";

    [Header("Cycling run (action = 2)")]

    [Tooltip("A number is appended to it: .../todos/1, .../todos/2, ...")]
    public string cycleBaseUrl = "https://dummyjson.com/todos/";

    [Tooltip("How many different urls in one lap")]
    public int cycleCount = 3;

    [Tooltip("Extra seconds between sends while urls are unknown. 0 = platform cooldown only")]
    public float cyclePeriod = 0f;

    [Tooltip("Extra seconds between sends once cached. 0 = platform cooldown only")]
    public float cachedPeriod = 0f;

    [Tooltip("Optional: status is written here")]
    public Text output;

    private bool awaitingResult;
    private bool cycling;
    private int cycleIndex;
    private int lap;
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
            awaitingResult = true;
            Log("Send sent: " + testUrl);

            return;
        }

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
        nextSendAt = Time.time;

        Log("cycle started: " + NumberOf(cycleCount) + " urls, one every " + cyclePeriod + "s");
    }

    public void OnQueryDone() => ShowResult();

    private void ShowResult()
    {
        if (client.LastError != "") { Log("ERROR\n" + client.LastError); return; }

        string body = client.TakeForwardedBody();

        if (!cycling) { lastResult = "done\n" + body; Log(lastResult); return; }

        bool cached = client.LastSendWasCached();

        nextSendAt = Time.time + (cached ? cachedPeriod : cyclePeriod);

        int ms = client.LastSendMs();

        if (!cached) { fullSendMs = ms; fullRequests = client.LastRequestCount(); }

        lastResult = Explain(cached, client.LastRequestCount(), ms) + "\n\n" + body;

        Log(lastResult);
    }

    private string lastResult = "";

    private int fullSendMs = -1;

    private string Explain(bool cached, int requests, int ms)
    {
        string head = "lap " + NumberOf(lap) + "   url " + NumberOf(cycleIndex == 0 ? cycleCount : cycleIndex)
                    + "/" + NumberOf(cycleCount) + "\n";

        if (cached)
        {

            string versus = fullSendMs > 0 ? "  vs " + NumberOf(fullSendMs) + " ms full" : "";

            return head
                 + "CACHED - 1 request - " + NumberOf(ms) + " ms" + versus + "\n\n"
                 + "The server kept a short handle for this url, so the whole thing\n"
                 + "fits in one request - one cooldown instead of " + NumberOf(fullRequests) + ".\n"
                 + CostTable(requests);
        }

        return head
             + "FULL SEND - " + NumberOf(requests) + " requests - " + NumberOf(ms) + " ms\n\n"
             + "VRChat only loads urls baked in at build time, so an arbitrary url is\n"
             + "spelled out " + NumberOf(client.SymbolsPerRune()) + " characters per request - and every request\n"
             + "pays the platform's string-load cooldown. That, not bandwidth, is the cost.\n"
             + CostTable(requests);
    }

    private string CostTable(int requests)
    {
        int rs = client.SymbolsPerRune();

        int symbols = (fullRequests > 1 ? fullRequests - 1 : requests - 1) * rs;

        string rows = "";

        for (int w = 2; w <= 4; w++)
        {
            int runes = (symbols + w - 1) / w + 1;

            rows += "   " + NumberOf(w) + " symbols/rune    " + NumberOf(runes) + " requests"
                  + (w == rs ? "   <- now\n" : "\n");
        }

        return "\n" + NumberOf(symbols) + " symbols after base compression:\n"
             + rows
             + "   cached            1 request";
    }

    private int fullRequests = 0;

    private string lastSeen = "";

    void Update()
    {
        if (client == null) return;

        if (awaitingResult && !client.IsBusy())
        {
            awaitingResult = false;

            ShowResult();
            return;
        }

        Tick();

        string now;

        if (client.LastError != "") now = "ERROR\n" + client.LastError;
        else if (!client.IsInitialized()) now = "not initialized - touch the blue cube";
        else if (client.IsBusy()) return;
        else if (cycling) return;
        else now = "ready - touch the green cube to start the demo\ninit: " + client.InitInfo;

        if (now == lastSeen) return;

        lastSeen = now;

        Log(now);
    }

    private void Tick()
    {
        if (!cycling) return;

        if (client.IsBusy()) return;

        if (Time.time < nextSendAt) return;

        string url = cycleBaseUrl + NumberOf(cycleIndex + 1);

        client.Send(url);
        awaitingResult = true;

        Log("lap " + NumberOf(lap) + "   sending " + url + "\n\n" + lastResult);

        nextSendAt = Time.time + cyclePeriod;

        cycleIndex++;

        if (cycleIndex < cycleCount) return;

        cycleIndex = 0;
        lap++;
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

    private void Log(string msg)
    {
        Debug.Log("[CombineQueriesTest] " + msg);

        if (output != null) output.text = msg;
    }
}
