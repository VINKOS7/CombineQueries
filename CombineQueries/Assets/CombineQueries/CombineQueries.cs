using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;

public class CombineQueries : UdonSharpBehaviour
{
    private const string baseUrl = CombineQueriesEnvironment.BaseUrl;

    private const string baseForwardUrl = "vink0s.com";

    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789-._~:/?#[]@!$&'()*+,;=%";
    private const string RuneAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789-._~:@!$&'()*+,;=";
    private const string Digits = "0123456789";
    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    // Корни (L1) больше не хардкодятся: строки приходят с сервера в connect (resp.roots),
    // клиент печёт только их КОЛИЧЕСТВО (FragmentCount) для рун-пространства. Пусто до init.
    private string[] roots = new string[0];

    private readonly string[] DirectFragments = new string[] { "", "o", ".com/", "." };

    private const int DirectPieces = 4;

    private const int FragmentCount = 35;
    private const int Symbols = 59 + FragmentCount;

    private const string AlphabetEncoded = "abcdefghijklmnopqrstuvwxyz0123456789-._~%3A%2F%3F%23%5B%5D%40%21%24%26%27%28%29%2A%2B%2C%3B%3D%25";

    private const string Scheme = "https";

    private const string Token = CombineQueriesEnvironment.Token;

    private const string AuthAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
    private const bool RequireCode = CombineQueriesEnvironment.RequireCode;

    private const int RuneSize = 3;
    private const string RuneSizeStr = "3";
    private const int RuneWidth = 4;
    private const int NumSize = 4;

    private const int MaxChunks = 256;
    private const int MaxHandles = 4096;

    // Размер адресной цепи фрагментов: сколько /f/-слотов печём и сообщаем серверу в /init.
    // Сервер не выдаёт id >= dfaSize. Меняешь размер - правишь обе константы (число и строку).
    private const int dfaSize = 4096;
    private const string DfaSizeStr = "4096";

    private readonly VRCUrl[] ChunkPool = PoolOf(baseUrl + "/c/", Symbols, RuneAlphabet, RuneSize, RuneWidth);
    private readonly VRCUrl[] TailPool = TailPoolOf(baseUrl + "/t/", Symbols, RuneAlphabet, RuneSize, RuneWidth);
    private readonly VRCUrl[] DirectTailPool = DirectTailPoolOf(baseUrl + "/d/", 59, RuneAlphabet, RuneSize, RuneWidth);
    private readonly VRCUrl[] HandlePool = NumPoolOf(baseUrl + "/h/", MaxHandles);
    private readonly VRCUrl[] FragmentPool = NumPoolOf(baseUrl + "/f/", dfaSize);
    private readonly VRCUrl[] AuthPool = AuthPoolOf(baseUrl + "/k/", AuthAlphabet);
    private readonly VRCUrl VerifyQuery = new VRCUrl(baseUrl + "/kf");

    private readonly VRCUrl InitQuery = new VRCUrl(baseUrl + "/init?alphabet=" + AlphabetEncoded + "&baseQuery=" + baseForwardUrl + "&runeSize=" + RuneSizeStr + "&scheme=" + Scheme + "&token=" + Token + "&dfaSize=" + DfaSizeStr);

    [Header("Where to report completion (optional)")]
    public UdonSharpBehaviour target;
    public string onDoneEvent = "OnQueryDone";

    [Header("Codeword typed in-world before Init/Remember")]
    public string codeword = "";

    public string LastError = "";
    public string LastUrl = "";
    public int LastSymbols;
    public int LastQueries;

    private const int PhaseIdle = 0;
    private const int PhaseInit = 1;
    private const int PhaseChunks = 2;
    private const int PhaseTail = 3;
    private const int PhaseHandle = 4;
    private const int PhaseCode = 5;
    private const int PhaseVerify = 6;
    private const int PhaseFragment = 7;

    private int phase;
    private bool initOk;
    private bool busy;
    private bool chainInit;

    private int[] queue;
    private int[] queueKind;
    private int queueLen;
    private int queuePos;
    private bool fragments = true;

    private string pendingUrl = "";
    private string forwarded = "";

    private string[] cachedUrls = new string[0];
    private int[] cachedHandles = new int[0];

    // Словарь динамических фрагментов, зеркало серверного: id (совпадает со слотом /f/) -> подстрока.
    // Заполняется сидом из /init и пиггибэком из /t/. Использование (врезка /f/) - шаг 2.
    private string[] cachedFragments = new string[0];
    private int[] cachedFragIds = new int[0];

    public void Init()
    {
        if (busy) return;

        LastError = "";

        if (RuneAlphabet.Length != Alphabet.Length - 6) { Fail("RuneAlphabet must be Alphabet minus #%[]/?"); return; }

        if (!RequireCode) { busy = true; Load(PhaseInit, InitQuery); return; }

        StartCode(true);
    }

    public void Remember()
    {
        if (busy || !RequireCode) return;

        LastError = "";

        StartCode(false);
    }

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

    public void Request(string url) => Send(url, true);

    public void RequestDirect(string url) => Send(url, false);

    private void Send(string url, bool withFragments)
    {
        if (busy || string.IsNullOrEmpty(url)) return;

        if (!initOk) { Fail("Init has not run - call Init first, then Request"); return; }

        fragments = withFragments;

        string payload = PayloadOf(url);

        if (payload == "") { Fail("Init fixed the scheme to " + Scheme + ", this url asks for another one"); return; }

        string problem = ProblemWith(payload);

        if (problem != "") { Fail(problem + ": " + url); return; }

        int[] symbols = SymbolsOf(payload);

        if (symbols == null) { Fail("character outside the alphabet: " + url); return; }

        LastError = "";
        forwarded = "";
        pendingUrl = payload;
        LastUrl = url;
        LastSymbols = symbols.Length;
        LastQueries = 0;
        busy = true;

        if (!withFragments) { SendDirect(payload); return; }

        int handle = HandleOf(payload);

        if (handle < 0) { SendCombine(payload); return; }

        Load(PhaseHandle, HandlePool[handle]);
    }

    public string TakeForwardedBody() => StringField(forwarded, "response");

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

    // Комбайн с динамическими фрагментами (L1+L2). На границе руны (аккумулятор пуст) сперва
    // ищем самый длинный кэш-фрагмент -> шлём его как /f/<id> (одна ссылка вместо рун); иначе
    // набираем символы (L1: корень или буква) по RuneSize в чанк /c/. Фрагмент допускаем только
    // на границе руны, поэтому частичных чанков не бывает - хвост как обычно несёт 0/1/2 символа.
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

                for (int i = 0; i < cachedFragments.Length; i++)
                {
                    if (cachedFragments[i].Length <= flen || pos + cachedFragments[i].Length > payload.Length) continue;
                    if (payload.Substring(pos, cachedFragments[i].Length) != cachedFragments[i]) continue;

                    fid = cachedFragIds[i];
                    flen = cachedFragments[i].Length;
                }

                if (fid >= 0 && fid < dfaSize)
                {
                    if (count >= MaxChunks) { Fail("url needs more than " + MaxChunks + " chunks"); return; }

                    q[count] = fid; k[count] = 1; count++;
                    pos += flen;
                    continue;
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

        q[count] = tail; k[count] = 2; count++;

        queueLen = count;
        queue = new int[queueLen];
        queueKind = new int[queueLen];

        for (int i = 0; i < queueLen; i++) { queue[i] = q[i]; queueKind[i] = k[i]; }

        queuePos = 0;

        SendNext();
    }

    // Один символ рун-пространства на позиции: самый длинный корень (L1-фрагмент, индекс >= 59)
    // либо одиночная буква (индекс в Alphabet). -1 - символа нет в алфавите.
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

        if (kind == 0) { Load(PhaseChunks, ChunkPool[queue[queuePos]]); return; }

        if (kind == 1) { Load(PhaseFragment, FragmentPool[queue[queuePos]]); return; }

        Load(PhaseTail, fragments ? TailPool[queue[queuePos]] : DirectTailPool[queue[queuePos]]);
    }

    public override void OnStringLoadSuccess(IVRCStringDownload response)
    {
        if (phase == PhaseCode) { queuePos++; SendCode(); return; }

        if (phase == PhaseVerify)
        {
            if (chainInit) { Load(PhaseInit, InitQuery); return; }

            Done();
            return;
        }

        if (phase == PhaseInit) { initOk = true; SeedFromInit(response.Result); Done(); return; }

        if (phase == PhaseChunks || phase == PhaseFragment) { queuePos++; SendNext(); return; }

        if (phase == PhaseTail)
        {
            int handle = IntField(response.Result, "handle");

            if (handle >= 0 && handle < MaxHandles) Cache(pendingUrl, handle);

            LearnFragments(response.Result);

            forwarded = response.Result;

            Done();
            return;
        }

        if (!BoolField(response.Result, "known"))
        {
            Forget(pendingUrl);

            if (fragments) SendCombine(pendingUrl); else SendDirect(pendingUrl);

            return;
        }

        forwarded = response.Result;

        Done();
    }

    public override void OnStringLoadError(IVRCStringDownload result)
    {
        if (phase == PhaseInit) initOk = false;

        if (phase == PhaseVerify) { Fail("codeword rejected"); return; }

        Fail((result.ErrorCode == 0 ? "host unreachable (server not running?), " : "") + result.Error);
    }

    private void Load(int nextPhase, VRCUrl url)
    {
        phase = nextPhase;

        LastQueries++;

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

    private void Cache(string url, int handle)
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

    // connect-сид: тёплый словарь мастера из тела /init. Хайперы кладём в кэш ссылок (Cache сам
    // отсеет дубли), фрагменты - в словарь фрагментов. Пустые массивы = холодный сервер, это норма.
    private void SeedFromInit(string json)
    {
        if (!VRCJson.TryDeserializeFromJson(json, out DataToken root)) return;
        if (root.TokenType != TokenType.DataDictionary) return;

        DataDictionary dict = root.DataDictionary;

        if (dict.TryGetValue("hypers", out DataToken hypers) && hypers.TokenType == TokenType.DataList)
        {
            DataList list = hypers.DataList;

            for (int i = 0; i < list.Count; i++)
            {
                if (!list.TryGetValue(i, out DataToken item) || item.TokenType != TokenType.DataDictionary) continue;

                int handle = DictInt(item.DataDictionary, "handle");
                string url = DictString(item.DataDictionary, "url");

                if (handle >= 0 && handle < MaxHandles && url != "") Cache(url, handle);
            }
        }

        // Корни L1 с сервера: индекс = символ (59 + f) в рун-пространстве. Принимаем только если
        // распарсились все строки (иначе выравнивание индексов сломается — оставляем пусто).
        if (dict.TryGetValue("roots", out DataToken rootsTok) && rootsTok.TokenType == TokenType.DataList)
        {
            DataList list = rootsTok.DataList;
            string[] r = new string[list.Count];
            int n = 0;

            for (int i = 0; i < list.Count; i++)
                if (list.TryGetValue(i, out DataToken it) && it.TokenType == TokenType.String) { r[n] = it.String; n++; }

            if (n == list.Count) roots = r;
        }

        LearnFragmentList(dict);
    }

    // Новые фрагменты из ответа /t/ (пиггибэк): та же таблица, что и в сиде.
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

        for (int i = 0; i < list.Count; i++)
        {
            if (!list.TryGetValue(i, out DataToken item) || item.TokenType != TokenType.DataDictionary) continue;

            int id = DictInt(item.DataDictionary, "id");
            string text = DictString(item.DataDictionary, "text");

            if (id >= 0 && id < dfaSize && text != "") CacheFragment(id, text);
        }
    }

    private void CacheFragment(int id, string text)
    {
        for (int i = 0; i < cachedFragIds.Length; i++) if (cachedFragIds[i] == id) return;

        string[] texts = new string[cachedFragments.Length + 1];
        int[] ids = new int[cachedFragIds.Length + 1];

        for (int i = 0; i < cachedFragments.Length; i++) { texts[i] = cachedFragments[i]; ids[i] = cachedFragIds[i]; }

        texts[cachedFragments.Length] = text;
        ids[cachedFragIds.Length] = id;

        cachedFragments = texts;
        cachedFragIds = ids;
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

    private static VRCUrl[] PoolOf(string baseUri, int symbols, string runeAlph, int runeSize, int runeWidth)
    {
        int total = 1;

        for (int i = 0; i < runeSize; i++) total *= symbols;

        VRCUrl[] pool = new VRCUrl[total];

        for (int v = 0; v < total; v++) pool[v] = new VRCUrl(baseUri + RunesOf(v, runeAlph, runeWidth));

        return pool;
    }

    private static VRCUrl[] TailPoolOf(string baseUri, int symbols, string runeAlph, int runeSize, int runeWidth)
    {
        int pad = Alphabet.IndexOf(':');

        VRCUrl[] pool = new VRCUrl[1 + symbols + symbols * symbols];

        for (int v = 0; v < pool.Length; v++)
        {
            int first = v == 0 ? pad : (v <= symbols ? v - 1 : (v - 1 - symbols) / symbols);
            int second = v > symbols ? (v - 1 - symbols) % symbols : pad;

            int value = first * symbols + second;

            for (int i = 2; i < runeSize; i++) value = value * symbols + pad;

            pool[v] = new VRCUrl(baseUri + RunesOf(value, runeAlph, runeWidth));
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

    private static VRCUrl[] NumPoolOf(string baseUri, int total)
    {
        VRCUrl[] pool = new VRCUrl[total];

        for (int v = 0; v < total; v++) pool[v] = new VRCUrl(baseUri + RunesOf(v, Digits, NumSize));

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
