using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;

public class CombineQueries : UdonSharpBehaviour
{
    private const string baseUrl = "http://localhost:5017";
    private const string baseForwardUrl = "vink0s.com";

    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789-._~:/?#[]@!$&'()*+,;=%";
    private const string WireAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789-._~:/?@!$&'()*+,;=";
    private const string Digits = "0123456789";

    private readonly string[] Fragments = new string[]
    {
        "https://", "http://", "www.", ".com", ".org", ".net", ".ru", ".io", ".dev",
        "/api/", "/v1/", "/r/", "/comments/", ".html", ".php", ".json",
        "json", "html", "index", "search", "image", "video", "data", "list", "item",
        "page", "user", "admin", "name", "true", "false", "?id=", "&id=", "://", "com"
    };

    private const int FragmentCount = 35;
    private const int Symbols = 59 + FragmentCount;

    private const string AlphabetEncoded = "abcdefghijklmnopqrstuvwxyz0123456789-._~%3A%2F%3F%23%5B%5D%40%21%24%26%27%28%29%2A%2B%2C%3B%3D%25";

    private const int RuneSize = 3;
    private const string RuneSizeStr = "3";
    private const int WireSize = 4;
    private const int NumSize = 4;

    private const int MaxChunks = 256;
    private const int MaxHandles = 4096;

    private readonly VRCUrl[] ChunkPool = PoolOf(baseUrl + "/c?r=", Symbols, WireAlphabet, RuneSize, WireSize);
    private readonly VRCUrl[] TailPool = TailPoolOf(baseUrl + "/n?r=", Symbols, WireAlphabet, RuneSize, WireSize);
    private readonly VRCUrl[] HandlePool = NumPoolOf(baseUrl + "/h?r=", MaxHandles);

    private readonly VRCUrl InitQuery = new VRCUrl(baseUrl + "/init?alphabet=" + AlphabetEncoded + "&baseQuery=" + baseForwardUrl + "&runeSize=" + RuneSizeStr);

    [Header("Where to report completion (optional)")]
    public UdonSharpBehaviour target;
    public string onDoneEvent = "OnQueryDone";

    public string LastError = "";

    private const int PhaseIdle = 0;
    private const int PhaseInit = 1;
    private const int PhaseChunks = 2;
    private const int PhaseTail = 3;
    private const int PhaseHandle = 4;

    private int phase;
    private bool initOk;
    private bool busy;

    private int[] queue;
    private int queueLen;
    private int queuePos;

    private string pendingUrl = "";
    private string forwarded = "";

    private string[] cachedUrls = new string[0];
    private int[] cachedHandles = new int[0];

    public void Init()
    {
        if (busy) return;

        LastError = "";

        if (Fragments.Length != FragmentCount) { Fail("Fragments table and FragmentCount disagree"); return; }
        if (WireAlphabet.Length != Alphabet.Length - 4) { Fail("WireAlphabet must be Alphabet minus #%[]"); return; }

        busy = true;

        Load(PhaseInit, InitQuery);
    }

    public void Request(string url)
    {
        if (busy || string.IsNullOrEmpty(url)) return;

        if (!initOk) { Fail("Init has not run - call Init first, then Request"); return; }

        LastError = "";
        forwarded = "";
        pendingUrl = url;
        busy = true;

        int handle = HandleOf(url);

        if (handle < 0) { SendFull(url); return; }

        Load(PhaseHandle, HandlePool[handle]);
    }

    public string TakeForwardedBody() => StringField(forwarded, "response");

    private void SendFull(string url)
    {
        int[] symbols = SymbolsOf(url);

        if (symbols == null) { Fail("character outside the alphabet"); return; }

        queueLen = symbols.Length / RuneSize + 1;

        if (queueLen > MaxChunks) { Fail("url needs more than " + MaxChunks + " chunks"); return; }

        queue = new int[queueLen];

        for (int i = 0; i < queueLen - 1; i++)
        {
            int value = 0;

            for (int j = 0; j < RuneSize; j++) value = value * Symbols + symbols[i * RuneSize + j];

            queue[i] = value;
        }

        int rest = symbols.Length - (queueLen - 1) * RuneSize;

        if (rest == 0) queue[queueLen - 1] = 0;
        else if (rest == 1) queue[queueLen - 1] = 1 + symbols[symbols.Length - 1];
        else queue[queueLen - 1] = 1 + Symbols + symbols[symbols.Length - 2] * Symbols + symbols[symbols.Length - 1];

        queuePos = 0;

        SendNext();
    }

    private void SendNext()
    {
        if (queuePos < queueLen - 1) Load(PhaseChunks, ChunkPool[queue[queuePos]]);
        else Load(PhaseTail, TailPool[queue[queuePos]]);
    }

    public override void OnStringLoadSuccess(IVRCStringDownload response)
    {
        if (phase == PhaseInit) { initOk = true; Done(); return; }

        if (phase == PhaseChunks) { queuePos++; SendNext(); return; }

        if (phase == PhaseTail)
        {
            int handle = IntField(response.Result, "handle");

            if (handle >= 0 && handle < MaxHandles) Remember(pendingUrl, handle);

            forwarded = response.Result;

            Done();
            return;
        }

        if (!BoolField(response.Result, "known"))
        {
            Forget(pendingUrl);
            SendFull(pendingUrl);
            return;
        }

        forwarded = response.Result;

        Done();
    }

    public override void OnStringLoadError(IVRCStringDownload result)
    {
        if (phase == PhaseInit) initOk = false;

        Fail((result.ErrorCode == 0 ? "host unreachable (server not running?), " : "") + result.Error);
    }

    private void Load(int nextPhase, VRCUrl url)
    {
        phase = nextPhase;

        VRCStringDownloader.LoadUrl(url, this);
    }

    private void Done()
    {
        busy = false;
        phase = PhaseIdle;

        if (target != null && onDoneEvent != "") target.SendCustomEvent(onDoneEvent);
    }

    private void Fail(string reason)
    {
        LastError = reason;

        Debug.LogError("CombineQueries: " + reason);

        Done();
    }

    private int HandleOf(string url)
    {
        for (int i = 0; i < cachedUrls.Length; i++) if (cachedUrls[i] == url) return cachedHandles[i];

        return -1;
    }

    private void Remember(string url, int handle)
    {
        if (HandleOf(url) >= 0) return;

        string[] urls = new string[cachedUrls.Length + 1];
        int[] handles = new int[cachedHandles.Length + 1];

        for (int i = 0; i < cachedUrls.Length; i++) { urls[i] = cachedUrls[i]; handles[i] = cachedHandles[i]; }

        urls[cachedUrls.Length] = url;
        handles[cachedHandles.Length] = handle;

        cachedUrls = urls;
        cachedHandles = handles;
    }

    private void Forget(string url)
    {
        string[] urls = new string[cachedUrls.Length];
        int[] handles = new int[cachedHandles.Length];
        int kept = 0;

        for (int i = 0; i < cachedUrls.Length; i++)
        {
            if (cachedUrls[i] == url) continue;

            urls[kept] = cachedUrls[i];
            handles[kept] = cachedHandles[i];
            kept++;
        }

        cachedUrls = new string[kept];
        cachedHandles = new int[kept];

        for (int i = 0; i < kept; i++) { cachedUrls[i] = urls[i]; cachedHandles[i] = handles[i]; }
    }

    private int IntField(string json, string field)
    {
        if (!VRCJson.TryDeserializeFromJson(json, out DataToken root)) return -1;
        if (root.TokenType != TokenType.DataDictionary) return -1;
        if (!root.DataDictionary.TryGetValue(field, out DataToken value)) return -1;

        return value.TokenType == TokenType.Double ? (int)value.Double : -1;
    }

    private string StringField(string json, string field)
    {
        if (!VRCJson.TryDeserializeFromJson(json, out DataToken root)) return "";
        if (root.TokenType != TokenType.DataDictionary) return "";
        if (!root.DataDictionary.TryGetValue(field, out DataToken value)) return "";

        return value.TokenType == TokenType.String ? value.String : "";
    }

    private bool BoolField(string json, string field)
    {
        if (!VRCJson.TryDeserializeFromJson(json, out DataToken root)) return false;
        if (root.TokenType != TokenType.DataDictionary) return false;
        if (!root.DataDictionary.TryGetValue(field, out DataToken value)) return false;

        return value.TokenType == TokenType.Boolean && value.Boolean;
    }

    private static VRCUrl[] PoolOf(string baseUri, int symbols, string wireAlph, int runeSize, int wireSize)
    {
        int total = 1;

        for (int i = 0; i < runeSize; i++) total *= symbols;

        VRCUrl[] pool = new VRCUrl[total];

        for (int v = 0; v < total; v++) pool[v] = new VRCUrl(baseUri + RunesOf(v, wireAlph, wireSize));

        return pool;
    }

    private static VRCUrl[] TailPoolOf(string baseUri, int symbols, string wireAlph, int runeSize, int wireSize)
    {
        int pad = Alphabet.IndexOf(':');

        VRCUrl[] pool = new VRCUrl[1 + symbols + symbols * symbols];

        for (int v = 0; v < pool.Length; v++)
        {
            int first = v == 0 ? pad : (v <= symbols ? v - 1 : (v - 1 - symbols) / symbols);
            int second = v > symbols ? (v - 1 - symbols) % symbols : pad;

            int value = first * symbols + second;

            for (int i = 2; i < runeSize; i++) value = value * symbols + pad;

            pool[v] = new VRCUrl(baseUri + RunesOf(value, wireAlph, wireSize));
        }

        return pool;
    }

    private static VRCUrl[] NumPoolOf(string baseUri, int total)
    {
        VRCUrl[] pool = new VRCUrl[total];

        for (int v = 0; v < total; v++) pool[v] = new VRCUrl(baseUri + RunesOf(v, Digits, NumSize));

        return pool;
    }

    private static string RunesOf(int value, string alph, int width)
    {
        string runes = "";

        for (int d = 0; d < width; d++)
        {
            runes = alph[value % alph.Length] + runes;
            value /= alph.Length;
        }

        return runes;
    }

    private int[] SymbolsOf(string url)
    {
        int[] buffer = new int[url.Length];
        int count = 0, position = 0;

        while (position < url.Length)
        {
            int best = -1, bestLength = 0;

            for (int f = 0; f < Fragments.Length; f++)
            {
                if (Fragments[f].Length <= bestLength || position + Fragments[f].Length > url.Length) continue;
                if (url.Substring(position, Fragments[f].Length) != Fragments[f]) continue;

                best = f;
                bestLength = Fragments[f].Length;
            }

            int letter = best < 0 ? Alphabet.IndexOf(url[position]) : -1;

            if (best < 0 && letter < 0) return null;

            buffer[count] = best < 0 ? letter : Alphabet.Length + best;
            position += best < 0 ? 1 : bestLength;
            count++;
        }

        int[] symbols = new int[count];

        for (int i = 0; i < count; i++) symbols[i] = buffer[i];

        return symbols;
    }
}
