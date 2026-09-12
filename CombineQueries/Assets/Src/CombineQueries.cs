using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;

public class CombineQueries : UdonSharpBehaviour
{
    [Header("Codeword typed in-world before Init/Remember")]
    public string codeword = "";

    [Header("Where to report completion (optional)")]
    public UdonSharpBehaviour target;

    private const bool rememberInfinite = true;
    private const bool RequireCode = CombineQueriesEnvironment.RequireCode;

    private const int PhaseIdle = 0;
    private const int PhaseConnect = 1;
    private const int PhaseChunks = 2;
    private const int PhaseTail = 3;
    private const int PhaseCode = 5;
    private const int PhaseVerify = 6;
    private const int PhaseFragment = 7;
    private const int PhaseJump = 8;
    private const int PhaseHead = 9;
    private const int PhaseCredit = 10;
    private const int MaxRemembered = 1024;

    private const string baseForwardUrl = "vink0s.com";
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789-._~:/?#[]@!$&'()*+,;=%";
    private const string RuneAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789-._~:@!$&'()*+,;=";
    private const string Digits = "0123456789";
    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string AlphabetEncoded = "abcdefghijklmnopqrstuvwxyz0123456789-._~%3A%2F%3F%23%5B%5D%40%21%24%26%27%28%29%2A%2B%2C%3B%3D%25";
    private const string Scheme = "https";
    private const string AuthAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
    private const string RememberInfiniteStr = "true";
    private const string RuneSizeStr = "3";
    private const string DfaSizeStr = "1024";
    private const string PageCountStr = "64";
    private const string HopCountStr = "64";
    private const string baseUrl = CombineQueriesEnvironment.BaseUrl;
    private const string GrowHypersStr = CombineQueriesEnvironment.MemHypers;
    private const string Token = CombineQueriesEnvironment.Token;

    private const int DirectPieces = 4;
    private const int FragmentCount = 35;
    private const int Symbols = 59 + FragmentCount;
    private const int RuneSize = 3;
    private const int RuneWidth = 4;
    private const int NumSize = 4;
    private const int MaxChunks = 256;
    private const int MaxJumps = 4096;
    private const int dfaSize = 1024;
    private const int pageCount = 64;
    private const int hopCount = 64;
    private const int SignValues = 8;
    private const int HeadLimit = 2048;
    private const int HeadBases = 8;
    private const int JumpSignValues = SignValues;
    private const int RangeMax = 4;
    private const int CloseLimit = 1024;

    private string[] roots = new string[0];
    private readonly string[] DirectFragments = new string[] { "", "o", ".com/", "." };

#if CQ_PROD
            private const bool resetHypers = false;
    private const string ResetHypersStr = "false";
#else
    private const string ResetHypersStr = "true";
#endif

    private readonly VRCUrl[] ChunkPool = PoolOf(baseUrl + "/c/", "/0/0/0/0", Symbols, RuneAlphabet, RuneSize, RuneWidth);
    private readonly VRCUrl[] TailPool = TailPoolOf(baseUrl + "/t/", Symbols, RuneAlphabet, RuneSize, RuneWidth, SignValues);
    private readonly VRCUrl[] DirectTailPool = DirectTailPoolOf(baseUrl + "/d/", 59, RuneAlphabet, RuneSize, RuneWidth);
    private readonly VRCUrl[] VfPool = VfPoolOf(baseUrl + "/c/", RuneAlphabet, RuneWidth, dfaSize, pageCount);
    private readonly VRCUrl[] HopPool = HopPoolOf(baseUrl + "/c/", RuneAlphabet, RuneWidth, hopCount);
    private readonly VRCUrl[] HeadPool = HeadPoolOf(baseUrl + "/hd/", HeadLimit, HeadBases, JumpSignValues);
    private readonly VRCUrl[] RangePool = RangePoolOf(baseUrl + "/h/", MaxJumps, RangeMax, JumpSignValues);
    private readonly VRCUrl[] CreditPool = CreditPoolOf(baseUrl + "/tc/", JumpSignValues);
    private readonly VRCUrl[] ClosePool = ClosePoolOf(baseUrl + "/cf/", CloseLimit, SignValues);
    private readonly VRCUrl[] AuthPool = AuthPoolOf(baseUrl + "/k/", AuthAlphabet);
    private readonly VRCUrl VerifyQuery = new VRCUrl(baseUrl + "/kf");
    private readonly VRCUrl ConnectQuery = new VRCUrl(baseUrl + "/connect?alphabet=" + AlphabetEncoded + "&baseQuery=" + baseForwardUrl + "&runeSize=" + RuneSizeStr + "&scheme=" + Scheme + "&token=" + Token + "&dfaSize=" + DfaSizeStr + "&pageCount=" + PageCountStr + "&hopCount=" + HopCountStr + "&rememberInfinite=" + RememberInfiniteStr + "&resetHypers=" + ResetHypersStr + "&hypers=" + GrowHypersStr);
    private readonly VRCUrl RememberQuery = new VRCUrl(baseUrl + "/connect?alphabet=" + AlphabetEncoded + "&baseQuery=" + baseForwardUrl + "&runeSize=" + RuneSizeStr + "&scheme=" + Scheme + "&token=" + Token + "&dfaSize=" + DfaSizeStr + "&pageCount=" + PageCountStr + "&hopCount=" + HopCountStr + "&rememberInfinite=" + RememberInfiniteStr + "&resetHypers=false&hypers=" + GrowHypersStr);

    public int LastSymbols;
    public int LastQueries;
    public int Errors;
    public int LastChunks;
    public int LastL2;
    public int LastL3;
    public int LastInfinite;
    public int LastUrls;
    public int LastJump = -1;
    public int SeedJumps;

    public string LastSent = "";
    public string LastRoad = "";
    public string onDoneEvent = "OnQueryDone";
    public string LastError = "";
    public string LastUrl = "";

    private bool connectOk;
    private bool busy;
    private bool chainInit;
    private bool fragments = true;

    private int phase;
    private int queueLen;
    private int queuePos;
    private int signPos;
    private int jumpRingAt;

    private string pendingUrl = "";
    private string forwarded = "";
    private string signs = "";

    private bool[] queuedDirect = new bool[0];
    private bool[] batchDirect = new bool[0];

    private int[] queue;
    private int[] queueKind;
    private int[] cachedFragIds = new int[0];

    private string[] jumpRing = new string[MaxRemembered];
    private string[] cachedFragments = new string[0];

    private DataDictionary jumps = new DataDictionary();

    public void Connect()
    {
        if (busy) return;

        LastError = "";

        if (RuneAlphabet.Length != Alphabet.Length - 6) { Fail("RuneAlphabet must be Alphabet minus #%[]/?"); return; }

        roots = new string[0];
        cachedFragments = new string[0];
        cachedFragIds = new int[0];
        ForgetJumps();

        Begin(true);
    }

    public void RequestPair(string first, string second)
    {
        Require(first);
        Require(second);
    }

    public void ForgetJumps()
    {
        jumps = new DataDictionary();
        jumpRing = new string[MaxRemembered];
        jumpRingAt = 0;
    }

    public void ForgetJump(string url)
    {
        jumps.Remove(PayloadOf(url));
    }

        private void KeepJump(string url, int jump)
    {
        if (jumps.ContainsKey(url)) { jumps.SetValue(url, jump); return; }

        string evicted = jumpRing[jumpRingAt];

        if (evicted != null && evicted != "") jumps.Remove(evicted);

        jumpRing[jumpRingAt] = url;
        jumpRingAt = (jumpRingAt + 1) % MaxRemembered;

        jumps.SetValue(url, jump);
    }

                        public void Remember()
    {
        if (busy) return;

        LastError = "";

        Begin(false);
    }

            private void Begin(bool reset)
    {
        connectReset = reset;

        if (!RequireCode) { busy = true; Load(PhaseConnect, reset ? ConnectQuery : RememberQuery); return; }

        StartCode(true);
    }

        private bool connectReset;

    private void StartCode(bool chain)
    {
        chainInit = chain;

        queueLen = codeword.Length;
        queue = new int[queueLen];

        for (int i = 0; i < queueLen; i++)
        {
            int index = AuthAlphabet.IndexOf(codeword[i]);

            if (index < 0) { Fail("codeword must be lowercase letters and digits only"); return; }

            queue[i] = index;
        }

        busy = true;
        queuePos = 0;

        SendCode();
    }

    private void SendCode()
    {
        if (queuePos < queueLen) { Load(PhaseCode, AuthPool[queue[queuePos]]); return; }

        Load(PhaseVerify, VerifyQuery);
    }

    public void Request(string url) => Dispatch(url, true);

    public bool Queue(string url) => Enqueue(url, false);

    public DataList Require(string url) => Ask(url, false);

        public DataList RequireDirect(string url) => Ask(url, true);

    private DataList Ask(string url, bool direct)
    {
        string key = PayloadOf(url);

        DataList box = boxes.TryGetValue(key, out DataToken had) && had.TokenType == TokenType.DataList
            ? had.DataList
            : new DataList();

        if (box.Count == 0) box.Add("");

        boxes.SetValue(key, box);

        if (!Enqueue(url, direct)) return box;

        if (!bodies.ContainsKey(key)) bodies.SetValue(key, "");

        flush = true;

        if (!pendingFlush) { pendingFlush = true; SendCustomEventDelayedFrames(nameof(Flush), 1); }

        if (!busy && queued.Length >= RangeMax) Run();

        return box;
    }

        private DataDictionary boxes = new DataDictionary();

    private void Fill(string payload, string body)
    {
        if (!boxes.TryGetValue(payload, out DataToken had) || had.TokenType != TokenType.DataList) return;

        had.DataList.SetValue(0, body);
    }

    public string Result(DataList box)
    {
        if (box == null || box.Count == 0) return "";

        return box.TryGetValue(0, out DataToken body) && body.TokenType == TokenType.String ? body.String : "";
    }

                        private bool flush;

        private bool pendingFlush;

    private void Update()
    {
        if (!flush || busy || queued.Length == 0) return;

        Run();
    }

    public void Flush()
    {
        if (!flush || queued.Length == 0) { pendingFlush = false; return; }

        if (busy) { SendCustomEventDelayedSeconds(nameof(Flush), 0.25f); return; }

        pendingFlush = false;

        Run();
    }

    public bool QueueDirect(string url) => Enqueue(url, true);

    private bool Enqueue(string url, bool direct)
    {
        if (string.IsNullOrEmpty(url)) return false;

        if (queued.Length >= MaxQueued) return false;

        string[] grown = new string[queued.Length + 1];
        bool[] grownDirect = new bool[queued.Length + 1];

        for (int i = 0; i < queued.Length; i++) { grown[i] = queued[i]; grownDirect[i] = queuedDirect[i]; }

        grown[queued.Length] = url;
        grownDirect[queued.Length] = direct;

        queued = grown;
        queuedDirect = grownDirect;

        return true;
    }



    private const int MaxQueued = 2048;

    public void Run()
    {
        if (busy || queued.Length == 0) return;

                        flush = false;

        batch = queued;
        batchDirect = queuedDirect;
        BatchQueries = 0;
        LastSent = "";

        queued = new string[0];
        queuedDirect = new bool[0];
        done = new bool[batch.Length];

        NextInBatch();
    }

            private void NextInBatch()
    {
                                int start = -1;

        for (int i = 0; i < batch.Length; i++)
        {
            if (done[i] || batchDirect[i]) continue;

            int first = JumpOf(PayloadOf(batch[i]));

            if (first < 0) continue;

            if (start < 0 || first < start) start = first;
        }

        if (start >= 0) { SendRange(start, RangeMax); return; }

                for (int i = 0; i < batch.Length; i++)
            if (!done[i]) { Dispatch(batch[i], !batchDirect[i]); return; }

        batch = new string[0];

        Finish();
    }

    private void SendRange(int first, int length)
    {
        LastError = "";
        forwarded = "";
        forwardedBody = "";
        pendingUrl = "";
        LastQueries = 0;
        LastJump = first;
        busy = true;

                        LastRoad = "hyper";

        queueLen = 1;
        queue = new int[1];
        queueKind = new int[1];
        queue[0] = (first * RangeMax + length - 1) * JumpSignValues + NextSign();
        queueKind[0] = 5;
        queuePos = 0;

        SendNext();
    }

    private bool[] done = new bool[0];

    private void TakeDebt(string json)
    {
        if (!VRCJson.TryDeserializeFromJson(json, out DataToken root)) return;
        if (root.TokenType != TokenType.DataDictionary) return;

        DataDictionary answer = root.DataDictionary;

        LastPending = DictInt(answer, "pending");

        if (LastPending < 0) LastPending = 0;

        if (!answer.TryGetValue("ready", out DataToken list) || list.TokenType != TokenType.DataList) return;

        DataList ready = list.DataList;

                        DataDictionary touched = new DataDictionary();

                DataDictionary bodyLines = new DataDictionary();

        for (int i = 0; i < ready.Count; i++)
        {
            if (!ready.TryGetValue(i, out DataToken item) || item.TokenType != TokenType.DataDictionary) continue;

            string url = PayloadOf(DictString(item.DataDictionary, "url"));

            if (url == "") continue;

            string body = DictString(item.DataDictionary, "response");

            bodies.SetValue(url, body);

                                    Fill(url, body);

            Mark(url);

                        if (url == pendingUrl) forwardedBody = body;

                                    int from = -1;

            if (asking.TryGetValue(url, out DataToken owner) && owner.TokenType == TokenType.Int) from = owner.Int;

                                    Named(url, from < 0 ? "vresponse" : "vresponse[" + from + "]");

            string named = TailOf(url) + " " + body.Length + "b/" + DictInt(item.DataDictionary, "elapsedMs") + "ms";

            if (from >= 0)
            {
                string was = touched.TryGetValue(from, out DataToken had) && had.TokenType == TokenType.String ? had.String : "";

                touched.SetValue(from, was == "" ? named : was + ", " + named);

                                                string kept = bodyLines.TryGetValue(from, out DataToken was2) && was2.TokenType == TokenType.String ? was2.String : "";
                string shown = Cut(body);

                bodyLines.SetValue(from, kept == "" ? shown : kept + " | " + shown);
            }

            SettleUrl(url);
        }

                        DataList ids = touched.GetKeys();

        for (int k = 0; k < ids.Count; k++)
        {
            if (!ids.TryGetValue(k, out DataToken id) || !touched.TryGetValue(id, out DataToken paid)) continue;

            string shown = bodyLines.TryGetValue(id, out DataToken kept) && kept.TokenType == TokenType.String ? kept.String : "";

            Report(id, paid.String, shown);
        }
    }

            private void Report(DataToken id, string paid, string shown)
    {
        if (!openRequests.TryGetValue(id, out DataToken value) || value.TokenType != TokenType.DataList) return;

        DataList record = value.DataList;

        if (!record.TryGetValue(0, out DataToken kind) || kind.TokenType != TokenType.String) return;
        if (!record.TryGetValue(1, out DataToken list) || list.TokenType != TokenType.DataList) return;
        if (!record.TryGetValue(2, out DataToken flags) || flags.TokenType != TokenType.DataList) return;
        if (!record.TryGetValue(3, out DataToken at) || at.TokenType != TokenType.Float) return;

        DataList urls = list.DataList;
        DataList settled = flags.DataList;

                if (record.TryGetValue(4, out DataToken hadPaid) && hadPaid.TokenType == TokenType.String)
            record.SetValue(4, hadPaid.String == "" ? paid : hadPaid.String + ", " + paid);

        if (record.TryGetValue(5, out DataToken hadData) && hadData.TokenType == TokenType.String && shown != "")
            record.SetValue(5, hadData.String == "" ? shown : hadData.String + " | " + shown);

                        string allShown = record.TryGetValue(5, out DataToken allData) && allData.TokenType == TokenType.String ? allData.String : shown;

        int answers = record.TryGetValue(6, out DataToken had) && had.TokenType == TokenType.Int ? had.Int + 1 : 1;

        record.SetValue(6, answers);

        string owed = "";

        for (int i = 0; i < urls.Count; i++)
        {
            if (settled.TryGetValue(i, out DataToken done) && done.TokenType == TokenType.Boolean && done.Boolean) continue;
            if (!urls.TryGetValue(i, out DataToken url) || url.TokenType != TokenType.String) continue;

            owed = owed == "" ? TailOf(url.String) : owed + ", " + TailOf(url.String);
        }

        string spent = (int)((Time.time - at.Float) * 1000f) + " ms";

                                        string who = id.Int + kind.String;

                                        string peek = shown == "" ? "" : "   " + (shown.Length <= Peek ? shown : shown.Substring(0, Peek) + "...");

        Trace("response: " + who + " погашено [" + paid + "], ждём [" + (owed == "" ? "" : owed) + "]   " + spent + peek, false);

        Data(who + " response [" + Joined(urls) + "]", shown);

        if (owed != "") return;

                        Trace("vresponse: " + who + " " + Joined(urls) + "   " + urls.Count + " urls за "
            + answers + (answers == 1 ? " ответ" : " ответа") + "   " + spent, false);

        Data(who + " vresponse [" + Joined(urls) + "]", allShown);

        openRequests.Remove(id);
    }

            public int TotalQueries;

        public int LastPending;

            private DataDictionary asking = new DataDictionary();

        private string route = "";

                                                    private DataDictionary openRequests = new DataDictionary();

            private int vrequests;

                        private string outgoing = "";
    private string incoming = "";

    public string TakeRequests()
    {
        string all = outgoing;

        outgoing = "";

        return all;
    }

    public string TakeResponses()
    {
        string all = incoming;

        incoming = "";

        return all;
    }

            private string payloads = "";

    public string TakeData()
    {
        string all = payloads;

        payloads = "";

        return all;
    }

            private void Data(string who, string shown)
    {
        if (shown == "") return;

        payloads = payloads == "" ? who + " " + shown : payloads + "\n" + who + " " + shown;
    }

        private string Cut(string body)
    {
        string flat = body.Replace("\n", " ").Replace("\r", " ");

        return flat.Length <= DataCut ? flat : flat.Substring(0, DataCut) + "...";
    }

    private const int DataCut = 160;

        private const int Peek = 200;

    private void Trace(string line, bool request)
    {
        if (request) outgoing = outgoing == "" ? line : outgoing + "\n" + line;
        else incoming = incoming == "" ? line : incoming + "\n" + line;

                        Debug.Log("[CombineQueries] " + line);
    }

    private int OpenRequest(DataList urls)
    {
        if (urls.Count == 0) return -1;

        int id = ++vrequests;

        DataList settled = new DataList();

        for (int i = 0; i < urls.Count; i++) settled.Add(false);

        DataList record = new DataList();

        record.Add(route);
        record.Add(urls);
        record.Add(settled);

                                record.Add(lastLoadAt);

                record.Add("");
        record.Add("");
        record.Add(0);

        openRequests.SetValue(id, record);

                for (int i = 0; i < urls.Count; i++)
            if (urls.TryGetValue(i, out DataToken url) && url.TokenType == TokenType.String)
                asking.SetValue(url.String, id);

        Trace("vrequest: " + id + route + " " + Joined(urls), true);

        return id;
    }

    private void SettleUrl(string payload)
    {
        DataList ids = openRequests.GetKeys();

        for (int k = 0; k < ids.Count; k++)
        {
            if (!ids.TryGetValue(k, out DataToken id)) continue;
            if (!openRequests.TryGetValue(id, out DataToken value) || value.TokenType != TokenType.DataList) continue;

            DataList record = value.DataList;

            if (!record.TryGetValue(1, out DataToken list) || list.TokenType != TokenType.DataList) continue;
            if (!record.TryGetValue(2, out DataToken flags) || flags.TokenType != TokenType.DataList) continue;

            DataList urls = list.DataList;
            DataList settled = flags.DataList;

            for (int i = 0; i < urls.Count; i++)
                if (urls.TryGetValue(i, out DataToken url) && url.TokenType == TokenType.String && url.String == payload)
                    settled.SetValue(i, true);
        }
    }


    private string Joined(DataList urls)
    {
        string all = "";

        for (int i = 0; i < urls.Count; i++)
        {
            if (!urls.TryGetValue(i, out DataToken url) || url.TokenType != TokenType.String) continue;

            all = all == "" ? TailOf(url.String) : all + ", " + TailOf(url.String);
        }

        return all;
    }

    private void TakeSent(string json)
    {
        if (!VRCJson.TryDeserializeFromJson(json, out DataToken root)) return;
        if (root.TokenType != TokenType.DataDictionary) return;
        if (!root.DataDictionary.TryGetValue("sent", out DataToken list) || list.TokenType != TokenType.DataList) return;

        DataList sent = list.DataList;

        DataList asked = new DataList();

        for (int i = 0; i < sent.Count; i++)
        {
            if (!sent.TryGetValue(i, out DataToken item) || item.TokenType != TokenType.DataDictionary) continue;

            string url = DictString(item.DataDictionary, "url");
            int jump = DictInt(item.DataDictionary, "jump");

            if (url == "" || jump < 0) continue;

            KeepJump(PayloadOf(url), jump);

                                                Mark(PayloadOf(url));

            Named(url, "vrequest[" + TotalQueries + "] hyper");

            asked.Add(PayloadOf(url));
        }

        OpenRequest(asked);
    }

    private void Named(string url, string mark)
    {
        string named = TailOf(url) + " " + mark;

        LastSent = LastSent == "" ? named : LastSent + ", " + named;
    }

    private string TailOf(string url)
    {
        string payload = PayloadOf(url);
        int cut = payload.IndexOf("/");

        return cut < 0 || cut + 1 >= payload.Length ? payload : payload.Substring(cut + 1);
    }

    public void Settle()
    {
        if (busy) return;

        LastError = "";
        forwarded = "";

                        pendingUrl = "";
        LastQueries = 0;
        busy = true;

        queueLen = 1;
        queue = new int[1];
        queueKind = new int[1];
        queue[0] = NextSign();
        queueKind[0] = 7;
        queuePos = 0;

        SendNext();
    }

    public string BodyOf(string url)
    {
        if (!bodies.TryGetValue(PayloadOf(url), out DataToken body)) return "";

        return body.TokenType == TokenType.String ? body.String : "";
    }

        public int BatchQueries;

    private string[] queued = new string[0];
    private string[] batch = new string[0];
    private DataDictionary bodies = new DataDictionary();


    public void RequestDirect(string url) => Dispatch(url, false);

                                                    public bool Busy() => busy;

    private void Dispatch(string url, bool withFragments)
    {
        if (busy || string.IsNullOrEmpty(url)) return;

        if (!connectOk) { Fail("Init has not run - call Init first, then Request"); return; }

        fragments = withFragments;

        string payload = PayloadOf(url);

        if (payload == "") { Fail("Init fixed the scheme to " + Scheme + ", this url asks for another one"); return; }

        string problem = ProblemWith(payload);

        if (problem != "") { Fail(problem + ": " + url); return; }

        int[] symbols = SymbolsOf(payload);

        if (symbols == null) { Fail("character outside the alphabet: " + url); return; }

        LastError = "";
        forwarded = "";

                        forwardedBody = "";

                        if (batch.Length == 0) LastSent = "";

        pendingUrl = payload;
        LastUrl = url;
        LastSymbols = symbols.Length;
        LastQueries = 0;
        busy = true;

        headTried = false;
        LastRoad = "";

        if (withFragments) SendCombine(payload); else SendDirect(payload);
    }

            public string Take() => forwardedBody != "" ? forwardedBody : StringField(forwarded, "response");

    private string forwardedBody = "";

    private string PayloadOf(string url)
    {
        if (url.IndexOf(Scheme + "://") == 0) return url.Substring(Scheme.Length + 3);
        if (url.IndexOf("http://") == 0 || url.IndexOf("https://") == 0) return "";

        return url;
    }

    private string ProblemWith(string payload)
    {
        if (payload.IndexOf("/") == 0 || payload.IndexOf("?") == 0 || payload.IndexOf(":") == 0) return "url has no host";
        if (payload.IndexOf(".") < 0 && payload.IndexOf("localhost") != 0) return "url has no domain";

        for (int i = 0; i < payload.Length; i++)
        {
            if (Alphabet.IndexOf(payload[i]) >= 0) continue;

            string letter = payload.Substring(i, 1);

            if (letter == " ") return "url contains a space";
            if (Upper.IndexOf(payload[i]) >= 0) return "the alphabet is lowercase only, this url has " + letter;

            return "character outside the alphabet: " + letter;
        }

        return "";
    }

                    private void SendCombine(string payload)
    {
        int[] q = new int[MaxChunks + 1];
        int[] k = new int[MaxChunks + 1];
        int count = 0;

        int acc = 0, accLen = 0, pos = 0;

        while (pos < payload.Length)
        {
            if (accLen == 0)
            {
                int fid = -1, flen = 0;

                char here = payload[pos];

                for (int i = 0; i < cachedFragments.Length; i++)
                {
                    if (cachedFragments[i].Length <= flen || pos + cachedFragments[i].Length > payload.Length) continue;

                                                            if (cachedFragments[i][0] != here) continue;

                    if (payload.Substring(pos, cachedFragments[i].Length) != cachedFragments[i]) continue;

                    fid = cachedFragIds[i];
                    flen = cachedFragments[i].Length;
                }

                if (fid >= 0)
                {
                                                                                int capacity = dfaSize * pageCount;
                    int hop = fid / capacity;
                    int anchor = fid - hop * capacity;

                                                            int plain = (flen + RuneSize - 1) / RuneSize;
                    int cost = hop > 0 ? 2 : 1;

                    if (hop < hopCount && cost <= plain && count + cost <= MaxChunks)
                    {
                        q[count] = anchor; k[count] = 1; count++;

                        if (hop > 0) { q[count] = hop; k[count] = 3; count++; }

                        pos += flen;
                        continue;
                    }
                }
            }

            int symLen;
            int sym = NextSymbol(payload, pos, out symLen);

            if (sym < 0) { Fail("character outside the alphabet: " + payload); return; }

            acc = acc * Symbols + sym;
            accLen++;
            pos += symLen;

            if (accLen == RuneSize)
            {
                if (count >= MaxChunks) { Fail("url needs more than " + MaxChunks + " chunks"); return; }

                q[count] = acc; k[count] = 0; count++;

                acc = 0; accLen = 0;
            }
        }

        int tail = accLen == 0 ? 0 : (accLen == 1 ? 1 + acc : 1 + Symbols + acc);

        if (count >= MaxChunks) { Fail("url needs more than " + MaxChunks + " chunks"); return; }

                                                if (tail == 0 && count > 0 && k[count - 1] == 1 && q[count - 1] < CloseLimit) k[count - 1] = 8;
        else { q[count] = tail; k[count] = 2; count++; }

                        int jump = JumpOf(payload);

                        if (jump < 0 && !headTried && SendHead(payload)) return;

        LastJump = -1;

        int skip = 0;

                                if (jump >= 0 && jump < MaxJumps) skip = count;

                                        LastRoad = headTried
            ? (skip > 0 ? "head/hyper" : "head/combine")
            : (skip > 0 ? "hyper" : "combine");

        queueLen = count - skip + (skip > 0 ? 1 : 0);
        queue = new int[queueLen];
        queueKind = new int[queueLen];

        int at = 0;

        if (skip > 0)
        {
            queue[0] = jump; queueKind[0] = 4; at = 1;

            LastJump = jump;
        }

        for (int i = skip; i < count; i++) { queue[at] = q[i]; queueKind[at] = k[i]; at++; }

        queuePos = 0;

        SendNext();
    }

            private int NextSymbol(string url, int pos, out int len)
    {
        int best = -1, bestLen = 0;

        for (int f = 0; f < roots.Length; f++)
        {
            if (roots[f].Length <= bestLen || pos + roots[f].Length > url.Length) continue;
            if (url.Substring(pos, roots[f].Length) != roots[f]) continue;

            best = f;
            bestLen = roots[f].Length;
        }

        if (best >= 0) { len = bestLen; return Alphabet.Length + best; }

        len = 1;

        return Alphabet.IndexOf(url[pos]);
    }

    private void SendDirect(string payload)
    {
        LastJump = -1;
        LastRoad = "direct";

        int[] buffer = new int[payload.Length];
        int count = 0, at = 0;

        while (at < payload.Length)
        {
            int value = 0;

            for (int j = 0; j < RuneSize; j++)
            {
                value = value * Alphabet.Length + (at < payload.Length ? Alphabet.IndexOf(payload[at]) : Alphabet.IndexOf(':'));

                if (at < payload.Length) at++;
            }

            int piece = 0, pieceLength = 0;

            for (int f = 1; f < DirectPieces; f++)
            {
                if (DirectFragments[f].Length <= pieceLength || at + DirectFragments[f].Length >= payload.Length) continue;
                if (payload.Substring(at, DirectFragments[f].Length) != DirectFragments[f]) continue;

                piece = f;
                pieceLength = DirectFragments[f].Length;
            }

            at += pieceLength;

            buffer[count] = value * DirectPieces + piece;
            count++;
        }

        if (count > MaxChunks) { Fail("url needs more than " + MaxChunks + " chunks"); return; }

        queueLen = count;
        queue = new int[queueLen];
        queueKind = new int[queueLen];

        for (int i = 0; i < queueLen; i++) { queue[i] = buffer[i]; queueKind[i] = 0; }

        queue[queueLen - 1] /= DirectPieces;
        queueKind[queueLen - 1] = 2;

        queuePos = 0;

        SendNext();
    }

    private void SendNext()
    {
        int kind = queueKind[queuePos];

                route = kind == 0 || kind == 1 || kind == 3 ? "/c"
              : kind == 4 || kind == 5 ? "/h"
              : kind == 6 ? "/hd"
              : kind == 7 ? "/tc"
              : kind == 8 ? "/cf"
              : fragments ? "/t" : "/d";

        if (kind == 0) { Load(PhaseChunks, ChunkPool[queue[queuePos]]); return; }

        if (kind == 1) { Load(PhaseFragment, VfPool[queue[queuePos]]); return; }

                if (kind == 3) { Load(PhaseFragment, HopPool[queue[queuePos]]); return; }

                                if (kind == 4) { Load(PhaseJump, RangePool[(queue[queuePos] * RangeMax + RangeMax - 1) * JumpSignValues + NextSign()]); return; }

                if (kind == 5) { Load(PhaseJump, RangePool[queue[queuePos]]); return; }

                if (kind == 6) { Load(PhaseHead, HeadPool[queue[queuePos]]); return; }

                if (kind == 7) { Load(PhaseCredit, CreditPool[queue[queuePos]]); return; }

                if (kind == 8) { Load(PhaseTail, ClosePool[queue[queuePos] * SignValues + NextSign()]); return; }

                if (!fragments) { Load(PhaseTail, DirectTailPool[queue[queuePos]]); return; }

        Load(PhaseTail, TailPool[queue[queuePos] * SignValues + NextSign()]);
    }

        private int NextSign()
    {
        if (signs.Length == 0) return 0;

        int sign = signs[signPos] - '0';

        signPos = (signPos + 1) % signs.Length;

        return sign;
    }

    public override void OnStringLoadSuccess(IVRCStringDownload response)
    {
                                if (phase == PhaseJump) TakeSent(response.Result);
        else if (phase == PhaseHead) headTaken = TakeHead(response.Result);

                        
        TakeDebt(response.Result);

                if (phase == PhaseCredit) { LastUrls = 0; Done(); return; }

        if (phase == PhaseCode) { queuePos++; SendCode(); return; }

        if (phase == PhaseVerify)
        {
            if (chainInit) { Load(PhaseConnect, connectReset ? ConnectQuery : RememberQuery); return; }

            Done();
            return;
        }

        if (phase == PhaseConnect)
        {
            connectOk = true;

            SeedFromConnect(response.Result);

                                    Done();
            return;
        }

        if (phase == PhaseChunks || phase == PhaseFragment) { queuePos++; SendNext(); return; }

                        if (phase == PhaseHead)
        {
                        int taken = headTaken;

                                    if (forwardedBody != "")
            {
                LastChunks = 0;
                LastL2 = 0;
                LastL3 = 0;
                LastInfinite = 0;
                LastUrls = taken > 0 ? taken : 1;

                forwarded = response.Result;

                Done();
                return;
            }

                                    if (taken >= 0 && JumpOf(pendingUrl) >= 0)
            {
                SendCombine(pendingUrl);
                return;
            }

            if (taken >= 0)
            {
                LastChunks = 0;
                LastL2 = 0;
                LastL3 = 0;
                LastInfinite = 0;
                LastUrls = taken;

                forwarded = response.Result;

                Done();
                return;
            }

                                                string kept = StringField(response.Result, "kept");

            SendCombine(kept == "" ? pendingUrl : pendingUrl.Substring(0, pendingUrl.Length - kept.Length));
            return;
        }

                if (phase == PhaseJump)
        {
                                    if (!BoolField(response.Result, "known"))
            {
                jumps.Remove(pendingUrl);

                LastJump = -1;

                SendCombine(pendingUrl);
                return;
            }

            LastChunks = 0;
            LastL2 = 0;
            LastL3 = 0;
            LastInfinite = 0;

            LastUrls = IntField(response.Result, "urls");
            forwarded = response.Result;

            Done();
            return;
        }

        if (phase == PhaseTail)
        {
            RememberChain(IntField(response.Result, "leaf"));

            LastChunks = IntField(response.Result, "chunks");
            LastL2 = IntField(response.Result, "l2");
            LastL3 = IntField(response.Result, "l3");
            LastInfinite = IntField(response.Result, "infinite");

            LearnFragments(response.Result);

            LastUrls = 1;
            forwardedBody = "";
            forwarded = response.Result;

                                    Named(pendingUrl, "vrequest[" + TotalQueries + "] " + LastRoad);


            Done();
            return;
        }

        Done();
    }

    public override void OnStringLoadError(IVRCStringDownload result)
    {
        if (phase == PhaseConnect) connectOk = false;

        if (phase == PhaseVerify) { Fail("codeword rejected"); return; }

        Fail((result.ErrorCode == 0 ? "host unreachable (server not running?), " : "") + result.Error);
    }

                                private const float Timeout = 60f;

    private float lastLoadAt;

    private void Load(int nextPhase, VRCUrl url)
    {
        phase = nextPhase;

        LastQueries++;
        TotalQueries++;
        lastLoadAt = Time.time;

                                                        string at = nextPhase == PhaseConnect ? "/connect"
                  : nextPhase == PhaseCode ? "/k"
                  : nextPhase == PhaseVerify ? "/kf"
                  : route;

        if (nextPhase == PhaseTail && pendingUrl != "")
        {
            DataList one = new DataList();

            one.Add(pendingUrl);

            OpenRequest(one);
        }
        else if (nextPhase != PhaseJump && nextPhase != PhaseHead)
        {
            Trace("request: " + TotalQueries + at, true);
        }

        SendCustomEventDelayedSeconds(nameof(OnLoadTimeout), Timeout);

        VRCStringDownloader.LoadUrl(url, this);
    }

                    public void OnLoadTimeout()
    {
        if (!busy) return;

        if (Time.time - lastLoadAt < Timeout - 1f) return;

        Fail("no answer in " + Timeout + "s on phase " + phase + ", query " + LastQueries
            + " of " + queueLen + " for " + LastUrl + " - url blocked by the SDK or server unreachable");
    }

        private void RememberChain(int leaf)
    {
        if (leaf < 0 || pendingUrl == "") return;

        KeepJump(pendingUrl, leaf);
    }

                        private bool SendHead(string payload)
    {
        int cut = payload.LastIndexOf("/");

        if (cut < 0 || cut + 1 >= payload.Length) return false;

                string differs = payload.Substring(cut + 1);
        string common = payload.Substring(0, cut + 1);

        int piece = -1;

        for (int i = 0; i < cachedFragments.Length; i++)
            if (cachedFragments[i] == differs && cachedFragIds[i] < HeadLimit) { piece = cachedFragIds[i]; break; }

        if (piece < 0) return false;

                        int found = -1;

        DataList keys = jumps.GetKeys();

        for (int i = 0; i < keys.Count; i++)
        {
            if (!keys.TryGetValue(i, out DataToken key) || key.TokenType != TokenType.String) continue;
            if (key.String.Length <= cut || key.String.Substring(0, cut + 1) != common) continue;

            found = JumpOf(key.String);

            if (found >= 0) break;
        }

        if (found < 0) return false;

        headTried = true;
        LastRoad = "head";

        queueLen = 1;
        queue = new int[1];
        queueKind = new int[1];
        queue[0] = (piece * HeadBases + (found % HeadBases)) * JumpSignValues + NextSign();
        queueKind[0] = 6;
        queuePos = 0;

        SendNext();

        return true;
    }

            private bool headTried;

            private int headTaken;

                    private int TakeHead(string json)
    {
        if (!VRCJson.TryDeserializeFromJson(json, out DataToken root)) return -1;
        if (root.TokenType != TokenType.DataDictionary) return -1;
        if (!root.DataDictionary.TryGetValue("found", out DataToken list) || list.TokenType != TokenType.DataList) return -1;

        DataList found = list.DataList;

        string mine = Scheme + "://" + pendingUrl;
        int ours = -1;

        DataList asked = new DataList();

        for (int i = 0; i < found.Count; i++)
        {
            if (!found.TryGetValue(i, out DataToken item) || item.TokenType != TokenType.DataDictionary) continue;

            string url = DictString(item.DataDictionary, "url");
            int jump = DictInt(item.DataDictionary, "jump");

            if (url == "" || jump < 0) continue;

            KeepJump(PayloadOf(url), jump);

            Named(url, "head[" + TotalQueries + "]");

            asked.Add(PayloadOf(url));

            if (url == mine) ours = found.Count;
        }

                        int head = OpenRequest(asked);

        for (int i = 0; i < asked.Count; i++)
            if (asked.TryGetValue(i, out DataToken url) && url.TokenType == TokenType.String) SettleUrl(url.String);

        if (head > 0) Report(head, "имена", "");

        return ours;
    }

                private int JumpOf(string url)
    {
        if (!jumps.TryGetValue(url, out DataToken value)) return -1;

        return value.TokenType == TokenType.Int ? value.Int : -1;
    }

    private void Done()
    {
        busy = false;
        phase = PhaseIdle;

        BatchQueries += LastQueries;

        if (batch.Length > 0)
        {
            TakeBatch();

            if (LastError == "") { NextInBatch(); return; }

            batch = new string[0];
        }

        Finish();

        if (LastPending > 0) SendCustomEventDelayedSeconds(nameof(Settle), CreditDelay);
    }

    private const float CreditDelay = 0.5f;

    private void TakeBatch()
    {
        if (pendingUrl == "") return;

        if (!bodies.ContainsKey(pendingUrl)) bodies.SetValue(pendingUrl, Take());

        Fill(pendingUrl, Take());

        Mark(pendingUrl);
    }

    private void Mark(string payload)
    {
        for (int i = 0; i < batch.Length; i++)
            if (!done[i] && PayloadOf(batch[i]) == payload) { done[i] = true; return; }
    }

    private void Finish()
    {
        if (target == null || onDoneEvent == "")
        {
            Debug.Log("[CombineQueries] done (target is not assigned), queries " + LastQueries + ", error: " + (LastError == "" ? "none" : LastError));
            return;
        }

        target.SendCustomEvent(onDoneEvent);
    }

    private void Fail(string reason)
    {
        LastError = reason;
        Errors++;

        Debug.LogError("CombineQueries: " + reason);

        Done();
    }

    private void SeedFromConnect(string json)
    {
        if (!VRCJson.TryDeserializeFromJson(json, out DataToken root)) return;
        if (root.TokenType != TokenType.DataDictionary) return;

        DataDictionary dict = root.DataDictionary;

        SeedJumps = 0;

                        if (dict.TryGetValue("roots", out DataToken rootsTok) && rootsTok.TokenType == TokenType.DataList)
        {
            DataList list = rootsTok.DataList;
            string[] r = new string[list.Count];
            int n = 0;

            for (int i = 0; i < list.Count; i++)
                if (list.TryGetValue(i, out DataToken it) && it.TokenType == TokenType.String) { r[n] = it.String; n++; }

            if (n == list.Count) roots = r;
        }

                        signs = DictString(dict, "signs");
        signPos = 0;

        SeedJumpList(dict);
        LearnFragmentList(dict);
    }

            private void SeedJumpList(DataDictionary dict)
    {
        if (!dict.TryGetValue("jumps", out DataToken seed) || seed.TokenType != TokenType.DataList) return;

        DataList list = seed.DataList;

        for (int i = 0; i < list.Count; i++)
        {
            if (!list.TryGetValue(i, out DataToken item) || item.TokenType != TokenType.DataDictionary) continue;

            string url = DictString(item.DataDictionary, "url");
            int jump = DictInt(item.DataDictionary, "jump");

            if (url == "" || jump < 0) continue;

            KeepJump(url, jump);
            SeedJumps++;
        }
    }

        private void LearnFragments(string json)
    {
        if (!VRCJson.TryDeserializeFromJson(json, out DataToken root)) return;
        if (root.TokenType != TokenType.DataDictionary) return;

        LearnFragmentList(root.DataDictionary);
    }

                private void LearnFragmentList(DataDictionary dict)
    {
        if (!dict.TryGetValue("fragments", out DataToken fragments) || fragments.TokenType != TokenType.DataList) return;

        DataList list = fragments.DataList;

        int have = cachedFragIds.Length;

        string[] texts = new string[have + list.Count];
        int[] ids = new int[have + list.Count];

        for (int i = 0; i < have; i++) { texts[i] = cachedFragments[i]; ids[i] = cachedFragIds[i]; }

        int n = have;

        for (int i = 0; i < list.Count; i++)
        {
            if (!list.TryGetValue(i, out DataToken item) || item.TokenType != TokenType.DataDictionary) continue;

            int id = DictInt(item.DataDictionary, "id");
            string text = DictString(item.DataDictionary, "text");

                                    if (id < 0 || text == "") continue;

                        bool known = false;

            for (int j = 0; j < have; j++) if (ids[j] == id) { known = true; break; }

            if (known) continue;

            texts[n] = text;
            ids[n] = id;
            n++;
        }

        if (n == texts.Length) { cachedFragments = texts; cachedFragIds = ids; return; }

                string[] fitTexts = new string[n];
        int[] fitIds = new int[n];

        for (int i = 0; i < n; i++) { fitTexts[i] = texts[i]; fitIds[i] = ids[i]; }

        cachedFragments = fitTexts;
        cachedFragIds = fitIds;
    }

    private int DictInt(DataDictionary dict, string field)
    {
        if (!dict.TryGetValue(field, out DataToken value)) return -1;

        return value.TokenType == TokenType.Double ? (int)value.Double : -1;
    }

    private string DictString(DataDictionary dict, string field)
    {
        if (!dict.TryGetValue(field, out DataToken value)) return "";

        return value.TokenType == TokenType.String ? value.String : "";
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

    private static VRCUrl[] PoolOf(string baseUri, string suffix, int symbols, string runeAlph, int runeSize, int runeWidth)
    {
        int total = 1;

        for (int i = 0; i < runeSize; i++) total *= symbols;

        VRCUrl[] pool = new VRCUrl[total];

        for (int v = 0; v < total; v++) pool[v] = new VRCUrl(baseUri + RunesOf(v, runeAlph, runeWidth) + suffix);

        return pool;
    }

    private static VRCUrl[] VfPoolOf(string baseUri, string runeAlph, int runeWidth, int slots, int pages)
    {
        string sentinel = RunesOf(0, runeAlph, runeWidth);

        VRCUrl[] pool = new VRCUrl[slots * pages];

        for (int p = 0; p < pages; p++)
            for (int o = 0; o < slots; o++)
                pool[p * slots + o] = new VRCUrl(baseUri + sentinel + "/" + o + "/" + p + "/0/1");

        return pool;
    }

                private static VRCUrl[] HopPoolOf(string baseUri, string runeAlph, int runeWidth, int hops)
    {
        string sentinel = RunesOf(0, runeAlph, runeWidth);

        VRCUrl[] pool = new VRCUrl[hops];

        for (int h = 0; h < hops; h++) pool[h] = new VRCUrl(baseUri + sentinel + "/0/0/" + h + "/1");

        return pool;
    }

            private static VRCUrl[] TailPoolOf(string baseUri, int symbols, string runeAlph, int runeSize, int runeWidth, int signs)
    {
        int pad = Alphabet.IndexOf(':');

        int tails = 1 + symbols + symbols * symbols;

        VRCUrl[] pool = new VRCUrl[tails * signs];

        for (int v = 0; v < tails; v++)
        {
            int first = v == 0 ? pad : (v <= symbols ? v - 1 : (v - 1 - symbols) / symbols);
            int second = v > symbols ? (v - 1 - symbols) % symbols : pad;

            int value = first * symbols + second;

            for (int i = 2; i < runeSize; i++) value = value * symbols + pad;

            string runes = RunesOf(value, runeAlph, runeWidth);

            for (int sign = 0; sign < signs; sign++) pool[v * signs + sign] = new VRCUrl(baseUri + runes + "/" + sign);
        }

        return pool;
    }

    private static VRCUrl[] DirectTailPoolOf(string baseUri, int symbols, string runeAlph, int runeSize, int runeWidth)
    {
        int total = 1;

        for (int i = 0; i < runeSize; i++) total *= symbols;

        VRCUrl[] pool = new VRCUrl[total];

        for (int v = 0; v < total; v++) pool[v] = new VRCUrl(baseUri + RunesOf(v * DirectPieces, runeAlph, runeWidth));

        return pool;
    }

            private static VRCUrl[] NumPoolOf(string baseUri, int total, int signs)
    {
        VRCUrl[] pool = new VRCUrl[total * signs];

        for (int v = 0; v < total; v++)
        {
            string num = RunesOf(v, Digits, NumSize);

            for (int sign = 0; sign < signs; sign++) pool[v * signs + sign] = new VRCUrl(baseUri + num + "/" + sign);
        }

        return pool;
    }

            private static VRCUrl[] RangePoolOf(string baseUri, int jumps, int max, int signs)
    {
        VRCUrl[] pool = new VRCUrl[jumps * max * signs];

        for (int jump = 0; jump < jumps; jump++)
        {
            string first = RunesOf(jump, Digits, NumSize);

            for (int count = 1; count <= max; count++)
            {
                string length = RunesOf(count, Digits, NumSize);

                for (int sign = 0; sign < signs; sign++)
                    pool[(jump * max + count - 1) * signs + sign] = new VRCUrl(baseUri + first + "/" + length + "/" + sign);
            }
        }

        return pool;
    }

    private static VRCUrl[] ClosePoolOf(string baseUri, int pieces, int signs)
    {
        VRCUrl[] pool = new VRCUrl[pieces * signs];

        for (int piece = 0; piece < pieces; piece++)
        {
            string id = RunesOf(piece, Digits, NumSize);

            for (int sign = 0; sign < signs; sign++) pool[piece * signs + sign] = new VRCUrl(baseUri + id + "/" + sign);
        }

        return pool;
    }

    private static VRCUrl[] CreditPoolOf(string baseUri, int signs)
    {
        VRCUrl[] pool = new VRCUrl[signs];

        for (int sign = 0; sign < signs; sign++) pool[sign] = new VRCUrl(baseUri + sign);

        return pool;
    }

            private static VRCUrl[] HeadPoolOf(string baseUri, int pieces, int bases, int signs)
    {
        VRCUrl[] pool = new VRCUrl[pieces * bases * signs];

        for (int piece = 0; piece < pieces; piece++)
        {
            string first = RunesOf(piece, Digits, NumSize);

            for (int b = 0; b < bases; b++)
            {
                string second = RunesOf(b, Digits, NumSize);

                for (int sign = 0; sign < signs; sign++)
                    pool[(piece * bases + b) * signs + sign] = new VRCUrl(baseUri + first + "/" + second + "/" + sign);
            }
        }

        return pool;
    }

    private static VRCUrl[] AuthPoolOf(string baseUri, string authAlphabet)
    {
        VRCUrl[] pool = new VRCUrl[authAlphabet.Length];

        for (int i = 0; i < authAlphabet.Length; i++) pool[i] = new VRCUrl(baseUri + authAlphabet[i]);

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

            for (int f = 0; fragments && f < roots.Length; f++)
            {
                if (roots[f].Length <= bestLength || position + roots[f].Length > url.Length) continue;
                if (url.Substring(position, roots[f].Length) != roots[f]) continue;

                best = f;
                bestLength = roots[f].Length;
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
