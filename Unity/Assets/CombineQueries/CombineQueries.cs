using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;

// URL forwarder client.
//
// First send of a url: /n (chunk count + padding) -> K times /m (one chunk each).
// The server assembles the url once K chunks arrive, forwards it and returns a HANDLE.
// Every later send of the same url: a single /h carrying that handle. That is the whole protocol.
//
// Why everything is const: VRCUrl can only be built from a constant expression, and string+int
// concatenation is not exposed in Udon - which is why numbers also go through RunesOf, not ToString.
public class CombineQueries : UdonSharpBehaviour
{
    private const string baseUrl = "http://localhost:5017";
    private const string baseForwardUrl = "vink0s.com";

    // Alphabet     - what the forwarded urls are made of (they contain / ? # %).
    // WireAlphabet - what the request itself may be written with: no # % [ ].
    //                / and ? stay, they are legal inside a query string.
    //
    // MUST match the server exactly. The server derives its own wire alphabet as
    // Alphabet minus "#%[]", so any extra or missing character here silently shifts the
    // numeric base and every chunk decodes to garbage - no error, just a wrong url.
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789-._~:/?#[]@!$&'()*+,;=%";
    private const string WireAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789-._~:/?@!$&'()*+,;=";
    private const string Digits = "0123456789";

    // Percent-encoded Alphabet for /init. It cannot be sent raw: '#' would start a fragment
    // and cut everything after it, '%' would start an escape sequence, and '&' '=' would be
    // parsed as query separators. Keep in sync with Alphabet above.
    private const string AlphabetEncoded = "abcdefghijklmnopqrstuvwxyz0123456789-._~%3A%2F%3F%23%5B%5D%40%21%24%26%27%28%29%2A%2B%2C%3B%3D%25";

    // TEMPORARILY 2 instead of 3: at 3 the chunk pool is 205 379 VRCUrl objects and building it
    // in a field initializer stalls world load. At 2 the pool is 3 481 - instant, and the whole
    // chain is still exercised. Raise to 3 (and WireSize to 4) once the startup cost is measured.
    private const int RuneSize = 2;     // source characters per chunk, 59^2 = 3 481
    private const string RuneSizeStr = "2";
    private const int WireSize = 3;     // wire digits per chunk: 55^2 = 3 025 < 3 481 <= 55^3
    private const int NumSize = 4;      // decimal digits in service values

    private const int MaxChunks = 256;  // url length ceiling: MaxChunks * RuneSize characters
    private const int MaxHandles = 4096;

    private readonly VRCUrl[] ChunkPool = PoolOf(baseUrl + "/m?r=", Alphabet, WireAlphabet, RuneSize, WireSize);
    private readonly VRCUrl[] CountPool = NumPoolOf(baseUrl + "/n?c=", MaxChunks * RuneSize + RuneSize);
    private readonly VRCUrl[] HandlePool = NumPoolOf(baseUrl + "/h?r=", MaxHandles);

    private readonly VRCUrl InitQuery = new VRCUrl(baseUrl + "/init?alphabet=" + AlphabetEncoded + "&baseQuery=" + baseForwardUrl + "&runeSize=" + RuneSizeStr);

    [Header("Where to report completion (optional)")]
    [SerializeField] private UdonSharpBehaviour target;
    [SerializeField] private string onDoneEvent = "OnQueryDone";

    // --- send state ---
    private int[] queue;
    private int queueLen;
    private int queuePos;
    private bool busy;
    private string pendingUrl = "";   // what is in flight right now, so the handle can be cached at the end

    private const int PhaseIdle = 0;
    private const int PhaseInit = 1;
    private const int PhaseCount = 2;
    private const int PhaseChunks = 3;
    private const int PhaseHandle = 4;
    private int phase;

    // --- url -> handle cache (parallel arrays: Dictionary is unreliable in Udon) ---
    private string[] cachedUrls = new string[0];
    private int[] cachedHandles = new int[0];

    public string InitInfo = string.Empty;
    public string WebContext = string.Empty;
    public string LastError = string.Empty;

    private bool initOk;
    private string forwarded = string.Empty;
    private bool hasForwarded;

    // How the last send actually travelled. Exposed so a demo can show the difference:
    // the cached path is not just faster on paper, it is one request instead of dozens.
    private bool lastCached;
    private int lastRequests;

    public bool IsInitialized() => initOk;
    public bool IsBusy() => busy;
    public bool HasResult() => hasForwarded;

    // true  = the url was already known to the server, sent as a single /h
    // false = full chain, /n plus one /m per chunk
    public bool LastSendWasCached() => lastCached;

    // How many http requests the last send cost. 1 when cached.
    public int LastRequestCount() => lastRequests;

    public string TakeResult()
    {
        if (!hasForwarded) return string.Empty;

        hasForwarded = false;

        return forwarded;
    }

    public void Init()
    {
        LastError = "";
        phase = PhaseInit;

        VRCStringDownloader.LoadUrl(InitQuery, this);
    }

    public void Send(string url)
    {
        if (busy) { Debug.LogWarning("CombineQueries: previous send is still in flight"); return; }
        if (string.IsNullOrEmpty(url)) return;

        if (!initOk)
        {
            LastError = "Init has not run - call Init first, then Send";
            Debug.LogError("CombineQueries: " + LastError);
            return;
        }

        pendingUrl = url;
        busy = true;

        // Already known to the server - the whole send collapses into a single request
        int cached = HandleOf(url);

        if (cached >= 0)
        {
            phase = PhaseHandle;
            lastCached = true;
            lastRequests = 1;

            VRCStringDownloader.LoadUrl(HandlePool[cached], this);
            return;
        }

        SendFull(url);
    }

    private void SendFull(string url)
    {
        // Pad up to a multiple of RuneSize. The filler character does not matter:
        // the server trims exactly 'pad' characters, without looking at what they are.
        int pad = (RuneSize - url.Length % RuneSize) % RuneSize;
        string padded = url;

        for (int i = 0; i < pad; i++) padded = padded + Alphabet[0];

        queueLen = padded.Length / RuneSize;

        if (queueLen > MaxChunks)
        {
            LastError = "Url is longer than the ceiling of " + MaxChunks * RuneSize + " characters";
            Debug.LogError("CombineQueries: " + LastError);
            busy = false;
            return;
        }

        queue = new int[queueLen];

        for (int i = 0; i < queueLen; i++)
        {
            int v = ValueOf(padded.Substring(i * RuneSize, RuneSize), Alphabet);

            if (v < 0)
            {
                LastError = "Character outside the alphabet";
                Debug.LogError("CombineQueries: " + LastError);
                busy = false;
                return;
            }

            queue[i] = v;
        }

        queuePos = 0;
        phase = PhaseCount;

        // Also covers the fallback from a stale handle: whatever the send started as,
        // once it lands here it is a full one.
        lastCached = false;
        lastRequests = queueLen + 1;      // one /n plus one /m per chunk

        // Chunk count and padding as one number - the server splits it back by RuneSize
        VRCStringDownloader.LoadUrl(CountPool[queueLen * RuneSize + pad], this);
    }

    // Strictly sequential: the next request only leaves from the previous one's callback.
    // Udon is single threaded and has no await, and chunk order matters on the server.
    private void SendNextChunk()
    {
        if (queuePos >= queueLen) { Done(); return; }

        VRCStringDownloader.LoadUrl(ChunkPool[queue[queuePos]], this);
    }

    public override void OnStringLoadSuccess(IVRCStringDownload response)
    {
        WebContext = response.Result;

        if (phase == PhaseInit)
        {
            InitInfo = response.Result;
            initOk = true;
            LastError = "";
            phase = PhaseIdle;
            return;
        }

        if (phase == PhaseCount) { phase = PhaseChunks; SendNextChunk(); return; }

        if (phase == PhaseChunks)
        {
            queuePos++;

            // Last chunk: the server has already assembled, forwarded and returned a handle
            if (queuePos >= queueLen)
            {
                int handle = IntField(response.Result, "handle");

                if (handle >= 0 && handle < MaxHandles) Remember(pendingUrl, handle);

                forwarded = response.Result;
                hasForwarded = true;

                Done();
                return;
            }

            SendNextChunk();
            return;
        }

        if (phase == PhaseHandle)
        {
            // The server may have restarted and lost its table - the cache is then lying,
            // so drop the entry and send the url in full.
            if (!BoolField(response.Result, "known"))
            {
                Debug.LogWarning("CombineQueries: handle is stale, resending the full url");

                Forget(pendingUrl);
                SendFull(pendingUrl);
                return;
            }

            forwarded = response.Result;
            hasForwarded = true;

            Done();
        }
    }

    public override void OnStringLoadError(IVRCStringDownload result)
    {
        if (phase == PhaseInit)
        {
            initOk = false;
            InitInfo = "";
            LastError = "NO CONNECTION TO SERVER (init): " + Describe(result);

            Debug.LogError("CombineQueries: init failed. " + Describe(result)
                         + "\nCheck that the server is running and the address matches: " + InitQuery.Get());

            phase = PhaseIdle;
            return;
        }

        LastError = "Send failed: " + Describe(result);

        Debug.LogError("CombineQueries: " + LastError);

        // Cannot continue: the server is left holding a half-filled buffer. It will not assemble
        // on its own (it waits for the declared K), so the next send starts from a fresh /n.
        Done();
    }

    private void Done()
    {
        busy = false;
        phase = PhaseIdle;

        if (target != null && onDoneEvent != "") target.SendCustomEvent(onDoneEvent);
    }

    private string Describe(IVRCStringDownload r)
    {
        // The numeric code is not formatted into the message: int->string is not exposed in Udon
        // (that has already caused a failure here). 0 means no connection happened at all -
        // the host is unreachable, as opposed to answering with an error.
        if (r.ErrorCode == 0) return "host unreachable (server not running?), " + r.Error;

        return r.Error;
    }

    // ---- cache ----

    private int HandleOf(string url)
    {
        for (int i = 0; i < cachedUrls.Length; i++) if (cachedUrls[i] == url) return cachedHandles[i];

        return -1;
    }

    private void Remember(string url, int handle)
    {
        if (HandleOf(url) >= 0) return;

        var u = new string[cachedUrls.Length + 1];
        var h = new int[cachedHandles.Length + 1];

        for (int i = 0; i < cachedUrls.Length; i++) { u[i] = cachedUrls[i]; h[i] = cachedHandles[i]; }

        u[cachedUrls.Length] = url;
        h[cachedHandles.Length] = handle;

        cachedUrls = u;
        cachedHandles = h;
    }

    private void Forget(string url)
    {
        int idx = -1;

        for (int i = 0; i < cachedUrls.Length; i++) if (cachedUrls[i] == url) { idx = i; break; }

        if (idx < 0) return;

        var u = new string[cachedUrls.Length - 1];
        var h = new int[cachedHandles.Length - 1];

        int j = 0;

        for (int i = 0; i < cachedUrls.Length; i++)
        {
            if (i == idx) continue;

            u[j] = cachedUrls[i];
            h[j] = cachedHandles[i];
            j++;
        }

        cachedUrls = u;
        cachedHandles = h;
    }

    // ---- response parsing ----

    private int IntField(string json, string field)
    {
        if (!VRCJson.TryDeserializeFromJson(json, out DataToken root)) return -1;
        if (root.TokenType != TokenType.DataDictionary) return -1;
        if (!root.DataDictionary.TryGetValue(field, out DataToken v)) return -1;
        if (v.TokenType != TokenType.Double) return -1;

        return (int)v.Double;
    }

    private bool BoolField(string json, string field)
    {
        if (!VRCJson.TryDeserializeFromJson(json, out DataToken root)) return false;
        if (root.TokenType != TokenType.DataDictionary) return false;
        if (!root.DataDictionary.TryGetValue(field, out DataToken v)) return false;
        if (v.TokenType != TokenType.Boolean) return false;

        return v.Boolean;
    }

    // ---- pure functions ----

    // Chunk pool: index == value (0 .. srcAlph^runeSize-1), the url is written in wireAlph digits
    private static VRCUrl[] PoolOf(string baseUri, string srcAlph, string wireAlph, int runeSize, int wireSize)
    {
        int total = 1;

        for (int i = 0; i < runeSize; i++) total *= srcAlph.Length;

        VRCUrl[] pool = new VRCUrl[total];

        for (int v = 0; v < total; v++) pool[v] = new VRCUrl(baseUri + RunesOf(v, wireAlph, wireSize));

        return pool;
    }

    // Pool of service numbers - decimal digits, through the same RunesOf (ToString is unavailable in Udon)
    private static VRCUrl[] NumPoolOf(string baseUri, int total)
    {
        VRCUrl[] pool = new VRCUrl[total];

        for (int v = 0; v < total; v++) pool[v] = new VRCUrl(baseUri + RunesOf(v, Digits, NumSize));

        return pool;
    }

    private static string RunesOf(int value, string alph, int width)
    {
        int len = alph.Length, rest = value;
        string runes = "";

        for (int d = 0; d < width; d++)
        {
            runes = alph[rest % len] + runes;
            rest /= len;
        }

        return runes;
    }

    private static int ValueOf(string runes, string alph)
    {
        int len = alph.Length, value = 0;

        foreach (char c in runes)
        {
            int digit = alph.IndexOf(c);

            if (digit < 0) return -1;

            value = value * len + digit;
        }

        return value;
    }
}
