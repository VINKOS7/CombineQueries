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

    // BASE COMPRESSION. A "symbol" is either one letter of Alphabet or one of these fragments,
    // so a single chunk can carry two letters - or two whole "https://"-sized pieces.
    //
    // This is the old alphabet-extension idea with one change that makes it work: the table is
    // STATIC. It is identical on both sides and never travels over the wire, so there is nothing
    // to synchronise - which is exactly what sank the growing-levels version. It also pays from
    // the very first send, with no warm-up.
    //
    // MUST match Translator.Fragments on the server, ORDER INCLUDED - the index is the contract.
    // Append only; inserting in the middle shifts every later symbol and breaks decoding silently.
    private readonly string[] Fragments = new string[]
    {
        "https://", "http://", "www.", ".com", ".org", ".net", ".ru", ".io", ".dev",
        "/api/", "/v1/", "/r/", "/comments/", ".html", ".php", ".json",
        "json", "html", "index", "search", "image", "video", "data", "list", "item",
        "page", "user", "admin", "name", "true", "false", "?id=", "&id=", "://", "com"
    };

    // Kept as a literal because pools are built in field initializers, before Fragments is usable
    // there. Guarded at Init(): a mismatch with Fragments.Length is reported instead of corrupting.
    private const int FragmentCount = 35;
    private const int Symbols = 59 + FragmentCount;   // 94

    // Percent-encoded Alphabet for /init. It cannot be sent raw: '#' would start a fragment
    // and cut everything after it, '%' would start an escape sequence, and '&' '=' would be
    // parsed as query separators. Keep in sync with Alphabet above.
    private const string AlphabetEncoded = "abcdefghijklmnopqrstuvwxyz0123456789-._~%3A%2F%3F%23%5B%5D%40%21%24%26%27%28%29%2A%2B%2C%3B%3D%25";

    // TEMPORARILY 2 instead of 3: at 3 the chunk pool is 205 379 VRCUrl objects and building it
    // in a field initializer stalls world load. At 2 the pool is 3 481 - instant, and the whole
    // chain is still exercised. Raise to 3 (and WireSize to 4) once the startup cost is measured.
    private const int RuneSize = 3;     // source SYMBOLS per chunk, 94^3 = 830 584
    private const string RuneSizeStr = "3";
    private const int WireSize = 4;     // wire digits: 55^3 = 166 375 < 830 584 <= 55^4
    private const int NumSize = 4;      // decimal digits in service values

    private const int MaxChunks = 256;  // url length ceiling: MaxChunks * RuneSize characters
    private const int MaxHandles = 4096;

    // Pool is now indexed by SYMBOL pairs, not letter pairs: 94^2 = 8 836 instead of 59^2 = 3 481.
    // Under 1 MB, and WireSize stays 3 because 55^3 = 166 375 still covers it.
    private readonly VRCUrl[] ChunkPool = PoolOf(baseUrl + "/m?r=", Symbols, WireAlphabet, RuneSize, WireSize);
    private readonly VRCUrl[] CountPool = NumPoolOf(baseUrl + "/n?c=", MaxChunks * RuneSize + RuneSize);
    private readonly VRCUrl[] HandlePool = NumPoolOf(baseUrl + "/h?r=", MaxHandles);

    private readonly VRCUrl InitQuery = new VRCUrl(baseUrl + "/init?alphabet=" + AlphabetEncoded + "&baseQuery=" + baseForwardUrl + "&runeSize=" + RuneSizeStr);

    // Public, not [SerializeField] private: an editor script that builds a rig has to be able to
    // wire this up. While they were private the callback could only be set by hand in the
    // inspector, so a generated rig silently never reported completion at all.
    [Header("Where to report completion (optional)")]
    public UdonSharpBehaviour target;
    public string onDoneEvent = "OnQueryDone";

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

    // Wall clock of the whole send, measured HERE. The server also reports its own timings,
    // but those miss the round trips to VRChat, which is where almost all the time actually goes.
    private float sendStartedAt;
    private int lastSendMs;

    public bool IsInitialized() => initOk;
    public bool IsBusy() => busy;
    public bool HasResult() => hasForwarded;

    // true  = the url was already known to the server, sent as a single /h
    // false = full chain, /n plus one /m per chunk
    public bool LastSendWasCached() => lastCached;

    // How many http requests the last send cost. 1 when cached.
    public int LastRequestCount() => lastRequests;

    // Milliseconds the last send took end to end, round trips included.
    public int LastSendMs() => lastSendMs;

    // Source characters carried by one request. Exposed so a demo can show the cost model
    // instead of hardcoding numbers that would silently rot when RuneSize changes.
    public int ChunkSize() => RuneSize;

    // The server answers with an envelope around the forwarded body. This is the body itself -
    // what the target url actually replied. Empty if the envelope has no usable "response".
    public string TakeForwardedBody()
    {
        if (!hasForwarded) return string.Empty;

        hasForwarded = false;

        return StringField(forwarded, "response");
    }

    public string TakeResult()
    {
        if (!hasForwarded) return string.Empty;

        hasForwarded = false;

        return forwarded;
    }

    public void Init()
    {
        LastError = "";

        // The two hand-maintained duplicates in this file are the only way a silent, total
        // corruption can enter - a stray character in WireAlphabet already cost a week once.
        // Both are checked here, where it is one comparison and a clear message.
        if (Fragments.Length != FragmentCount)
        {
            LastError = "Fragments table and FragmentCount disagree - fix the constant";
            Debug.LogError("CombineQueries: " + LastError);
            return;
        }

        if (WireAlphabet.Length != Alphabet.Length - 4)
        {
            LastError = "WireAlphabet must be Alphabet minus the four unsafe characters";
            Debug.LogError("CombineQueries: " + LastError);
            return;
        }

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
        sendStartedAt = Time.time;

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
        int[] symbols = SymbolsOf(url);

        if (symbols == null)
        {
            LastError = "Character outside the alphabet";
            Debug.LogError("CombineQueries: " + LastError);
            busy = false;
            return;
        }

        // Pad the SYMBOL list up to a multiple of RuneSize with symbol 0, which is a single
        // character. That keeps the server's rule intact: it trims exactly 'pad' characters.
        int used = symbols.Length;
        int pad = (RuneSize - used % RuneSize) % RuneSize;

        queueLen = (used + pad) / RuneSize;

        if (queueLen > MaxChunks)
        {
            LastError = "Url needs more than " + MaxChunks + " chunks";
            Debug.LogError("CombineQueries: " + LastError);
            busy = false;
            return;
        }

        queue = new int[queueLen];

        for (int i = 0; i < queueLen; i++)
        {
            int v = 0;

            for (int j = 0; j < RuneSize; j++)
            {
                int at = i * RuneSize + j;

                v = v * Symbols + (at < used ? symbols[at] : 0);   // 0 = Alphabet[0], the pad symbol
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

        // Snapped before the callback fires, so a listener reads the finished number
        lastSendMs = (int)((Time.time - sendStartedAt) * 1000f);

        if (target != null && onDoneEvent != "") { target.SendCustomEvent(onDoneEvent); return; }

        // Loud on purpose. A missing target is invisible otherwise: the send succeeds, the result
        // sits in `forwarded`, and nobody ever reads it - which looks exactly like "nothing works".
        Debug.LogWarning("CombineQueries: nobody to notify - set `target` and `onDoneEvent`. "
                       + "The result is ready but will not be delivered.");
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

    private string StringField(string json, string field)
    {
        if (!VRCJson.TryDeserializeFromJson(json, out DataToken root)) return string.Empty;
        if (root.TokenType != TokenType.DataDictionary) return string.Empty;
        if (!root.DataDictionary.TryGetValue(field, out DataToken v)) return string.Empty;
        if (v.TokenType != TokenType.String) return string.Empty;

        return v.String;
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

    // Chunk pool: index == value (0 .. symbols^runeSize-1), the url is written in wireAlph digits
    private static VRCUrl[] PoolOf(string baseUri, int symbols, string wireAlph, int runeSize, int wireSize)
    {
        int total = 1;

        for (int i = 0; i < runeSize; i++) total *= symbols;

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

    // Greedy longest match: at each position take the longest fragment that fits, otherwise one
    // letter. Greedy is not provably optimal, but it is one pass and it never loses to plain
    // letters - and the whole point here is that it costs nothing at runtime.
    // Returns null if a character is outside Alphabet.
    private int[] SymbolsOf(string url)
    {
        int[] buf = new int[url.Length];      // upper bound: every symbol is one letter
        int n = 0, i = 0;

        while (i < url.Length)
        {
            int best = -1, bestLen = 0;

            for (int f = 0; f < Fragments.Length; f++)
            {
                string frag = Fragments[f];

                if (frag.Length <= bestLen) continue;
                if (i + frag.Length > url.Length) continue;
                if (url.Substring(i, frag.Length) != frag) continue;

                best = f;
                bestLen = frag.Length;
            }

            if (best >= 0)
            {
                buf[n] = Alphabet.Length + best;
                i += bestLen;
            }
            else
            {
                int letter = Alphabet.IndexOf(url[i]);

                if (letter < 0) return null;

                buf[n] = letter;
                i++;
            }

            n++;
        }

        int[] res = new int[n];

        for (int k = 0; k < n; k++) res[k] = buf[k];

        return res;
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
