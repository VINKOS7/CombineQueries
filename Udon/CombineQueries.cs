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

    private const int MaxRunes = 256;
    private const int MaxHandles = 4096;

    private readonly VRCUrl[] RunePool = PoolOf(baseUrl + "/m?r=", Symbols, WireAlphabet, RuneSize, WireSize);
    private readonly VRCUrl[] TailPool = NumPoolOf(baseUrl + "/n?c=", MaxRunes * RuneSize + RuneSize);
    private readonly VRCUrl[] HandlePool = NumPoolOf(baseUrl + "/h?r=", MaxHandles);

    private readonly VRCUrl InitQuery = new VRCUrl(baseUrl + "/init?alphabet=" + AlphabetEncoded + "&baseQuery=" + baseForwardUrl + "&runeSize=" + RuneSizeStr);

    [Header("Where to report completion (optional)")]
    public UdonSharpBehaviour target;
    public string onDoneEvent = "OnQueryDone";

    private int[] runes;
    private int runeCount;
    private int runeAt;
    private bool busy;
    private string pendingUrl = "";

    private const int PhaseIdle = 0;
    private const int PhaseInit = 1;
    private const int PhaseCount = 2;
    private const int PhaseRunes = 3;
    private const int PhaseHandle = 4;
    private int phase;

    private string[] cachedUrls = new string[0];
    private int[] cachedHandles = new int[0];

    public string InitInfo = string.Empty;
    public string LastError = string.Empty;

    private bool initOk;
    private string forwarded = string.Empty;
    private bool hasForwarded;

    private bool lastCached;
    private int lastRequests;

    private float sendStartedAt;
    private int lastSendMs;

    public bool IsInitialized() => initOk;
    public bool IsBusy() => busy;
    public bool HasResult() => hasForwarded;

    public bool LastSendWasCached() => lastCached;

    public int LastRequestCount() => lastRequests;

    public int LastSendMs() => lastSendMs;

    public int SymbolsPerRune() => RuneSize;

    public string TakeForwardedBody()
    {
        if (!hasForwarded) return string.Empty;

        hasForwarded = false;

        return StringField(forwarded, "response");
    }

    public void Init()
    {
        LastError = "";

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

        int used = symbols.Length;
        int pad = (RuneSize - used % RuneSize) % RuneSize;

        runeCount = (used + pad) / RuneSize;

        if (runeCount > MaxRunes)
        {
            LastError = "Url needs more than " + MaxRunes + " runes";
            Debug.LogError("CombineQueries: " + LastError);
            busy = false;
            return;
        }

        runes = new int[runeCount];

        for (int i = 0; i < runeCount; i++)
        {
            int v = 0;

            for (int j = 0; j < RuneSize; j++)
            {
                int at = i * RuneSize + j;

                v = v * Symbols + (at < used ? symbols[at] : 0);
            }

            runes[i] = v;
        }

        runeAt = 0;
        phase = PhaseCount;

        lastCached = false;
        lastRequests = runeCount + 1;

        VRCStringDownloader.LoadUrl(TailPool[runeCount * RuneSize + pad], this);
    }

    private void SendNextRune()
    {
        if (runeAt >= runeCount) { Done(); return; }

        VRCStringDownloader.LoadUrl(RunePool[runes[runeAt]], this);
    }

    public override void OnStringLoadSuccess(IVRCStringDownload response)
    {
        if (phase == PhaseInit)
        {
            InitInfo = response.Result;
            initOk = true;
            LastError = "";
            phase = PhaseIdle;
            return;
        }

        if (phase == PhaseCount) { phase = PhaseRunes; SendNextRune(); return; }

        if (phase == PhaseRunes)
        {
            runeAt++;

            if (runeAt >= runeCount)
            {
                int handle = IntField(response.Result, "handle");

                if (handle >= 0 && handle < MaxHandles) Remember(pendingUrl, handle);

                forwarded = response.Result;
                hasForwarded = true;

                Done();
                return;
            }

            SendNextRune();
            return;
        }

        if (phase == PhaseHandle)
        {

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

        Done();
    }

    private void Done()
    {
        busy = false;
        phase = PhaseIdle;

        lastSendMs = (int)((Time.time - sendStartedAt) * 1000f);

        if (target != null && onDoneEvent != "") { target.SendCustomEvent(onDoneEvent); return; }

        Debug.LogWarning("CombineQueries: nobody to notify - set `target` and `onDoneEvent`. "
                       + "The result is ready but will not be delivered.");
    }

    private string Describe(IVRCStringDownload r)
    {

        if (r.ErrorCode == 0) return "host unreachable (server not running?), " + r.Error;

        return r.Error;
    }

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

    private static VRCUrl[] PoolOf(string baseUri, int symbols, string wireAlph, int runeSize, int wireSize)
    {
        int total = 1;

        for (int i = 0; i < runeSize; i++) total *= symbols;

        VRCUrl[] pool = new VRCUrl[total];

        for (int v = 0; v < total; v++) pool[v] = new VRCUrl(baseUri + WiresOf(v, wireAlph, wireSize));

        return pool;
    }

    private static VRCUrl[] NumPoolOf(string baseUri, int total)
    {
        VRCUrl[] pool = new VRCUrl[total];

        for (int v = 0; v < total; v++) pool[v] = new VRCUrl(baseUri + WiresOf(v, Digits, NumSize));

        return pool;
    }

    private static string WiresOf(int value, string alph, int width)
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

    private int[] SymbolsOf(string url)
    {
        int[] buf = new int[url.Length];
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

}
