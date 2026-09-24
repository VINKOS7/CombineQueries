using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;

// Клиент сети не касается: у каждого игрока свой. Синхронизация выключена целиком - тогда и сетевые
// события на него не проходят, и чужой игрок не дёрнет Connect, Settle или любой другой public-метод.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class CombineQueries : UdonSharpBehaviour
{
#if CQ_PROD
    private const string ResetHypersStr = "false";
#else
    private const string ResetHypersStr = "false";
#endif

    public int LastSymbols;
    public int LastQueries;
    public int BatchQueries;
    public int TotalQueries;
    public int Answers;
    public int Errors;
    public int LastChunks;
    public int LastL2;
    public int LastL3;
    public int LastInfinite;
    public int LastUrls;
    public int LastJump = -1;
    public int LastPending;
    public int SeedJumps;

    [Header("Codeword typed in-world before Init/Remember")]
    public string codeword = "";
    // Чей это набор. Клиент один на весь мир, а просящих в нём несколько, и в общей консоли их
    // строки ложатся вперемешку. Метку ставит сам просящий перед Result, она садится в запись
    // vrequest и потом едет во всех его строках: vrequest, response, vresponse.
    public string who = "";

    public string LastSent = "";
    public string LastRoad = "";
    public string LastError = "";
    public string LastUrl = "";

    private const bool rememberInfinite = true;
    private const bool RequireCode = CombineQueriesEnvironment.RequireCode;

    private const string baseUrl = CombineQueriesEnvironment.BaseUrl;
    private const string GrowHypersStr = CombineQueriesEnvironment.MemHypers;
    private const string Token = CombineQueriesEnvironment.Token;
    private const string baseForwardUrl = "vink0s.com";
    private const string Scheme = "https";
    private const string RememberInfiniteStr = "true";
    private const string RuneSizeStr = "3";
    private const string DfaSizeStr = "1024";
    private const string PageCountStr = "64";
    private const string HopCountStr = "64";

    // ---- Алфавиты ----

    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789-._~:/?#[]@!$&'()*+,;=%";
    private const string RuneAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789-._~:@!$&'()*+,;=";
    private const string AlphabetEncoded = "abcdefghijklmnopqrstuvwxyz0123456789-._~%3A%2F%3F%23%5B%5D%40%21%24%26%27%28%29%2A%2B%2C%3B%3D%25";
    private const string AuthAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
    private const string Digits = "0123456789";
    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    // ---- Фазы загрузки ----

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

    // ---- Размеры, из которых запекаются пулы ----

    private const int DirectPieces = 4;
    private const int FragmentCount = 35;
    private const int Symbols = 59 + FragmentCount;
    private const int RuneSize = 3;
    private const int RuneWidth = 4;
    private const int NumSize = 4;
    private const int MaxChunks = 256;
    // Потолок номера прыжка. Половина прежних 4096 - это плата за вдвое широкое окно: пул стоит
    // MaxJumps * RangeMax * подписи, и обмен держит его прежним. На проде самый большой номер 1090,
    // запас двойной. Номер выше потолка не ломается, а теряет прыжок: такой адрес едет сборкой.
    private const int MaxJumps = 2048;
    private const int dfaSize = 1024;
    private const int pageCount = 64;
    private const int hopCount = 64;
    private const int SignValues = 8;
    private const int JumpSignValues = SignValues;
    private const int HeadLimit = 2048;
    private const int HeadBases = 8;
    // Ширина окна: сколько адресов семьи сервер отдаёт за один /h. Было 4, и в семью из 75 детей
    // (dummyjson.com/products/) попадала половина пачки - между products/1 и products/4 лежат
    // products/12, /11, /0, и они съедали бюджет. Восемь накрывают всю четвёрку за один запрос.
    // Больше не имеет смысла: у сервера Batch = 8.
    private const int RangeMax = 8;
    private const int CloseLimit = 1024;

    // ---- Лимиты, тайминги, лог ----

    private const int MaxRemembered = 1024;
    private const int MaxQueued = 2048;
    private const float Timeout = 60f;
    private const float CreditDelay = 0.5f;
    private const int DataCut = 160;
    private const int Peek = 200;

    // ---- Запечённые ссылки ----

    private readonly string[] DirectFragments = new string[] { "", "o", ".com/", "." };

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

    // ---- Подключение ----

    private bool connectOk;
    private bool connectReset;
    private bool chainInit;

    private int signPos;
    private string signs = "";

    private string[] roots = new string[0];
    private string[] cachedFragments = new string[0];
    private int[] cachedFragIds = new int[0];

    // ---- Текущая отправка ----

    private bool fragments = true;

    // Рукопожатие (кодовое слово и connect) идёт строго по одному запросу, его и ведём фазой.
    // Всё остальное узнаётся из самого ответа: запросов в полёте много, «текущего» не бывает.
    private int phase;
    private int queueLen;
    private int queuePos;
    private float lastLoadAt;

    private int[] queue;
    private int[] queueKind;

    private string route = "";
    private string pendingUrl = "";
    private string forwarded = "";
    private string forwardedBody = "";

    // Сколько запросов ещё не ответило. Занятость наружу считается по нему; очередь ОТПРАВКИ держит
    // сам VRCStringDownloader, повторять её здесь нечем и незачем.
    private int inFlight;

    // Запланирован ли добор долга - чтобы не плодить его на каждый ответ.
    private bool pendingSettle;

    // Номер vrequest текущей пачки: один на пачку, выдаётся при выпуске.
    private int batchRequest = -1;

    // ---- Таблица запросов ----
    //
    // Слот на адрес. Мир получает от Require ключ и по нему же спрашивает Result. Ожидания в таблице
    // нет: проход отправки пропускает слоты, которые уже в полёте, а пришедшее тело помечает слот
    // переиспользуемым - его и займёт первый же новый адрес.
    private string[] slotKey = new string[0];

    // Печёная ссылка, которой слот просили: по ней ответ находит хозяина, когда адреса в ответе нет.
    private string[] slotSent = new string[0];

    private bool[] slotLoading = new bool[0];
    private bool[] slotDelete = new bool[0];
    private bool[] slotDirect = new bool[0];

    // Голову по этому адресу уже звали: второй раз она не поможет.
    private bool[] slotHeaded = new bool[0];

    // Какой вопрос слот задал голове, -1 если не задавал. Вопрос - это пара «кусок расхождения,
    // найденный номер», и по нему же печётся ссылка: одинаковый вопрос даёт одинаковую ссылку.
    private int[] slotHead = new int[0];

    // Сколько запросов ушло на адрес, на каком запросе своего vrequest пришло тело, тот же запрос
    // номером с начала работы клиента, и номер запроса на выпуске.
    private int[] slotQueries = new int[0];
    private int[] slotAnswer = new int[0];
    private int[] slotGlobal = new int[0];
    private int[] slotReleased = new int[0];

    // Виды запроса: те же числа, что у queueKind, плюс служебные сверху.
    private const int KindChunk = 0;
    private const int KindFragment = 1;
    private const int KindTail = 2;
    private const int KindHop = 3;
    private const int KindJumpOne = 4;
    private const int KindRange = 5;
    private const int KindHead = 6;
    private const int KindCredit = 7;
    private const int KindClose = 8;

    // Единственный словарь тел: ключ - адрес без схемы, значение - пара «состояние, тело».
    //
    // Состояний три, и отсутствия записи среди них нет: запись заводит сам Require, поэтому «нет в
    // словаре» значит ровно одно - этот адрес не просили ни разу. Раньше состояние подменяла пустая
    // строка, и пустой ответ сервера был неотличим от неприехавшего: такой адрес ждали вечно.
    private const int StatusInit = 0;
    private const int StatusLoading = 1;
    private const int StatusLoaded = 2;

    private DataDictionary bodies = new DataDictionary();

    // ---- Прыжки ----

    private int jumpRingAt;
    private string[] jumpRing = new string[MaxRemembered];
    private DataDictionary jumps = new DataDictionary();

    // ---- Лог ----

    private int vrequests;
    private DataDictionary openRequests = new DataDictionary();
    private DataDictionary asking = new DataDictionary();
    private string outgoing = "";
    private string incoming = "";
    private string payloads = "";

    // ==== Публичный API ====

    // Прошёл ли connect. Только чтение, как Busy: риг, который сам connect не нажимал, по нему и
    // узнаёт, что можно начинать - события о завершении у клиента больше нет.
    public bool Connected() => connectOk;

    public bool Busy() => inFlight > 0;

    public void Connect()
    {
        if (inFlight > 0) return;

        LastError = string.Empty;

        if (RuneAlphabet.Length != Alphabet.Length - 6) { Fail("RuneAlphabet must be Alphabet minus #%[]/?"); return; }

        roots = new string[0];
        cachedFragments = new string[0];
        cachedFragIds = new int[0];

        ForgetJumps();

        Begin(true);
    }

    public void Remember()
    {
        if (inFlight > 0) return;

        LastError = "";

        Begin(false);
    }

    // Просит адрес и возвращает КЛЮЧ, по которому потом спросят результат. Сама ничего не шлёт:
    // отправку выпускает Result, и одним проходом по всей набранной пачке.
    public string Require(string url) => Ask(url, false);

    public string RequireDirect(string url) => Ask(url, true);

    // Результат по ключу. Первый вызов выпускает всё, что набрано и ещё не в полёте, дальше просто
    // читает словарь тел. Пустая строка значит либо «ещё едет», либо пустой ответ - различает их
    // StatusOf, и спрашивать о приезде надо им.
    public string Result(string key)
    {
        if (key == "") return "";

        Run();

        return BodyOf(key);
    }

    // Сколько запросов ушло на адрес.
    public int QueriesOf(string key)
    {
        int at = SlotOf(key);

        return at < 0 ? 0 : slotQueries[at];
    }

    // На каком запросе своего vrequest пришло тело: ответ на сам vrequest - первый.
    public int AnswerOf(string key)
    {
        int at = SlotOf(key);

        return at < 0 ? 0 : slotAnswer[at];
    }

    // Тот же ответ номером с начала работы клиента.
    public int GlobalOf(string key)
    {
        int at = SlotOf(key);

        return at < 0 ? 0 : slotGlobal[at];
    }

    public void Request(string url) => Dispatch(PayloadOf(url), true);

    public void RequestDirect(string url) => Dispatch(PayloadOf(url), false);

    // maybe depraceted
    //public void RequestPair(string first, string second)
    //{
    //    Require(first);
    //    Require(second);
    //}

    public string Take() => forwardedBody != "" ? forwardedBody : StringField(forwarded, "response");

    // Состояние адреса: -1 не просили ни разу, Init запись заведена, Loading запрос в пути,
    // Loaded тело свежее.
    public int StatusOf(string key) => bodies.TryGetValue(PayloadOf(key), out DataToken slot) && slot.TokenType == TokenType.DataList
        ? slot.DataList.TryGetValue(0, out DataToken status) && status.TokenType == TokenType.Int ? status.Int : -1
        : -1;

    // Состояние словом - для вывода. Пусто у того, чего ни разу не просили.
    //
    // Лесенкой, а не switch-выражением: UdonSharp компилирует C# 7.3, и рекурсивных образцов там
    // ещё нет - CS8370.
    public string StatusName(string key)
    {
        int status = StatusOf(key);

        return status == StatusInit ? "Init"
             : status == StatusLoading ? "Loading"
             : status == StatusLoaded ? "Loaded" : "";
    }

    // Приехало ли тело. Спрашивать этим, а не сравнением тела с пустотой: пустой ответ - тоже ответ.
    public bool Loaded(string key) => StatusOf(key) == StatusLoaded;

    // Тоже без switch-выражения, и заодно без дыры: образец `string key` не покрывал null, а
    // непокрытое значение в рантайме - исключение.
    public string BodyOf(string url)
    {
        string key = PayloadOf(url);

        return StatusOf(key) == StatusLoaded ? Stored(key) : "";
    }

    // Тело как лежит, без оглядки на состояние.
    private string Stored(string key)
    {
        if (!bodies.TryGetValue(key, out DataToken slot) || slot.TokenType != TokenType.DataList) return "";

        return slot.DataList.TryGetValue(1, out DataToken body) && body.TokenType == TokenType.String ? body.String : "";
    }

    // Кладёт состояние адреса. Прошлое тело при этом НЕ стирается: наружу его и так не видно -
    // BodyOf отдаёт только у Loaded, - а кешу неизменяемых ответов оно ещё пригодится.
    private void KeepBody(string key, int status, string body)
    {
        DataList slot = new DataList();

        slot.Add(status);
        slot.Add(body);

        bodies.SetValue(key, slot);
    }

    // Слот ушёл в полёт или вернулся ждать своего окна: Init <-> Loading.
    private void Flying(int at, bool flying)
    {
        slotLoading[at] = flying;

        KeepBody(slotKey[at], flying ? StatusLoading : StatusInit, Stored(slotKey[at]));
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

    public string TakeData()
    {
        string all = payloads;

        payloads = "";

        return all;
    }

    // ==== Подключение и кодовое слово ====

    private void Begin(bool reset)
    {
        connectReset = reset;

        if (!RequireCode) { LoadPhase(PhaseConnect, reset ? ConnectQuery : RememberQuery); return; }

        // for codegen, maybe deprecated
        StartCode(true);
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

        queuePos = 0;

        SendCode();
    }

    private void SendCode()
    {
        if (queuePos < queueLen) { LoadPhase(PhaseCode, AuthPool[queue[queuePos]]); return; }

        LoadPhase(PhaseVerify, VerifyQuery);
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

            int n = 0;
            string[] r = new string[list.Count];

            for (int i = 0; i < list.Count; i++) if (list.TryGetValue(i, out DataToken it) && it.TokenType == TokenType.String) r[n] = it.String; n++;

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

            int jump = DictInt(item.DataDictionary, "jump");

            string url = DictString(item.DataDictionary, "url");

            if (url == "" || jump < 0) continue;

            KeepJump(url, jump);
            SeedJumps++;
        }
    }

    // ==== Таблица запросов ====

    private string Ask(string url, bool direct)
    {
        string key = PayloadOf(url);

        if (key == "") { Fail("Init fixed the scheme to " + Scheme + ", this url asks for another one"); return ""; }

        int at = SlotOf(key);

        if (at < 0) at = FreeSlot();

        slotKey[at] = key;
        slotSent[at] = "";
        slotLoading[at] = false;
        slotDelete[at] = false;
        slotDirect[at] = direct;
        slotHeaded[at] = false;
        slotHead[at] = -1;
        slotQueries[at] = 0;
        slotAnswer[at] = 0;
        slotGlobal[at] = 0;
        slotReleased[at] = TotalQueries;

        KeepBody(key, StatusInit, Stored(key));

        return key;
    }

    // Слот адреса. Сперва живой, а если такого нет - переиспользуемый с тем же ключом: тело уже
    // приехало, но мир ещё читает по нему счётчики, и ноли вместо них были бы враньём.
    private int SlotOf(string key)
    {
        int spare = -1;

        for (int i = 0; i < slotKey.Length; i++)
        {
            if (slotKey[i] != key) continue;

            if (!slotDelete[i]) return i;

            if (spare < 0) spare = i;
        }

        return spare;
    }

    // Слот по печёной ссылке, которой его просили. Живой в приоритете: у переиспользуемого слота
    // ссылка осталась с прошлой жизни, и отдавать ему чужой ответ нельзя.
    private int SlotBySent(string link)
    {
        int spare = -1;

        for (int i = 0; i < slotKey.Length; i++)
        {
            if (slotSent[i] != link) continue;

            if (!slotDelete[i]) return i;

            if (spare < 0) spare = i;
        }

        return spare;
    }

    // Первый переиспользуемый слот, а если таких нет - таблица растёт на один.
    private int FreeSlot()
    {
        for (int i = 0; i < slotKey.Length; i++) if (slotDelete[i]) return i;
        
        int size = slotKey.Length + 1;

        bool[] loading = new bool[size];
        bool[] deleted = new bool[size];
        bool[] direct = new bool[size];
        bool[] headed = new bool[size];

        int[] head = new int[size];
        int[] queries = new int[size];
        int[] answer = new int[size];
        int[] global = new int[size];
        int[] released = new int[size];

        string[] key = new string[size];
        string[] sent = new string[size];

        for (int i = 0; i < slotKey.Length; i++)
        {
            key[i] = slotKey[i];
            sent[i] = slotSent[i];
            loading[i] = slotLoading[i];
            deleted[i] = slotDelete[i];
            direct[i] = slotDirect[i];
            headed[i] = slotHeaded[i];
            head[i] = slotHead[i];
            queries[i] = slotQueries[i];
            answer[i] = slotAnswer[i];
            global[i] = slotGlobal[i];
            released[i] = slotReleased[i];
        }

        slotKey = key;
        slotSent = sent;
        slotLoading = loading;
        slotDelete = deleted;
        slotDirect = direct;
        slotHeaded = headed;
        slotHead = head;
        slotQueries = queries;
        slotAnswer = answer;
        slotGlobal = global;
        slotReleased = released;

        slotHead[size - 1] = -1;

        return size - 1;
    }

    // Выпуск пачки: уходит всё, что набрано и ещё не в полёте, и уходит сразу.
    private void Run()
    {
        DataList urls = new DataList();

        for (int i = 0; i < slotKey.Length; i++) if (!slotDelete[i] && !slotLoading[i] && slotKey[i] != "") urls.Add(slotKey[i]);

        if (urls.Count == 0) return;

        // Мир спрашивает Result каждый кадр, поэтому об отсутствии подключения говорим ОДИН раз,
        // пока отказ не разобрали. Набранное при этом не теряется - уедет первым же Result после connect.
        if (!connectOk)
        {
            if (LastError == "") Fail("Init has not run - call Init first, then Require");

            return;
        }

        LastSent = "";
        BatchQueries = 0;
        batchRequest = OpenRequest(urls);

        for (int i = 0; i < slotKey.Length; i++) if (!slotDelete[i] && !slotLoading[i] && slotKey[i] != "") slotReleased[i] = TotalQueries;

        Jumps();
        Spell();
    }

    // Прыжки: ОДНО окно на пачку.
    //
    // Раньше здесь считалось, что окно накрывает соседей по НОМЕРУ, и на каждую дырку в номерах
    // уходило своё окно: четыре адреса - четыре запроса по пять секунд. Но сервер отдаёт не
    // соседей по номеру, а СЕМЬЮ узла - тех, кто отличается ровно последним шагом. Номера у них
    // какие угодно, и заранее эту четвёрку не вычислить.
    //
    // Поэтому размечаем окном всех, у кого номер есть, и ждём ответа: он и скажет, кого накрыло.
    private void Jumps()
    {
        int anchor = -1, best = 0, last = 0;

        for (int i = 0; i < slotKey.Length; i++)
        {
            if (slotDelete[i] || slotLoading[i] || slotDirect[i] || slotKey[i] == "") continue;

            int jump = JumpOf(slotKey[i]);

            if (jump < 0 || jump >= MaxJumps) continue;

            // Семья отдаётся по возрастанию номера, поэтому якорь - самый младший: с него окно
            // накрывает больше всего своих. Самый старший нужен, чтобы знать ширину окна.
            if (anchor < 0 || jump < best) { anchor = i; best = jump; }

            if (jump > last) last = jump;
        }

        if (anchor < 0) return;

        // Просим СТОЛЬКО, СКОЛЬКО НАДО, а не максимум. Раньше окно всегда запрашивало RangeMax, и
        // на пачке из двух адресов сервер послушно шёл наружу за восемью: шесть тел никто не
        // просил, и каждое стоило ему похода в чужой API.
        //
        // Ширину берём по размаху номеров, а не по числу адресов: сервер отдаёт БРАТЬЕВ подряд,
        // и между нашими могут стоять чужие. Размах их накрывает с запасом и не промахивается,
        // потому что порядковый номер брата не бывает больше разницы номеров.
        int count = last - best + 1;

        if (count > RangeMax) count = RangeMax;

        VRCUrl link = RangePool[(best * RangeMax + count - 1) * JumpSignValues + NextSign()];
        string sent = link.Get();

        for (int i = 0; i < slotKey.Length; i++)
        {
            if (slotDelete[i] || slotLoading[i] || slotDirect[i] || slotKey[i] == "") continue;

            int other = JumpOf(slotKey[i]);

            if (other < 0 || other >= MaxJumps) continue;

            Flying(i, true);
            slotSent[i] = sent;
        }

        LastJump = best;
        LastRoad = "hyper";
        route = "/h";

        Load(KindRange, "", link);
    }

    // Остальные адреса диктуются сами: голова или сборка. Тоже сразу, без ожидания чужого ответа.
    private void Spell()
    {
        for (int i = 0; i < slotKey.Length; i++)
        {
            if (slotDelete[i] || slotLoading[i] || slotKey[i] == "") continue;

            Flying(i, true);

            Dispatch(slotKey[i], !slotDirect[i]);
        }
    }

    // Тело приехало: слот гасит полёт и становится переиспользуемым.
    private void Mark(string payload)
    {
        int at = SlotOf(payload);

        if (at < 0) return;

        slotLoading[at] = false;
        slotDelete[at] = true;
    }

    // Прыжок не узнан: адреса, которые он должен был накрыть, просим заново - сборкой.
    private void Reopen(string link)
    {
        for (int i = 0; i < slotKey.Length; i++)
        {
            if (slotDelete[i] || slotSent[i] != link) continue;

            Flying(i, false);
            slotSent[i] = "";
        }

        Spell();
    }

    private void Fail(string reason)
    {
        LastError = reason;
        Errors++;

        Debug.LogError("CombineQueries: " + reason);
    }

    // Во что обошлась отправка адреса.
    private void Sent(string payload, int queries)
    {
        int at = SlotOf(payload);

        if (at >= 0) slotQueries[at] = queries;
    }

    // Тело пришло: запоминаем, на каком запросе это случилось.
    private void Fill(string payload, string body)
    {
        int at = SlotOf(payload);

        if (at < 0) return;

        // Номер внутри своей пачки - номер ОТВЕТА, а не тела. Два адреса, приехавшие вместе,
        // делят его честно; порядок внутри одного ответа задаёт очередь долга, и нумеровать по
        // нему значило бы показывать миру случайность. Пачку узнаём по отсечке выпуска.
        int same = 0, top = 0;

        for (int i = 0; i < slotKey.Length; i++)
        {
            if (i == at || slotAnswer[i] <= 0 || slotReleased[i] != slotReleased[at]) continue;

            if (slotGlobal[i] == Answers) same = slotAnswer[i];

            if (slotAnswer[i] > top) top = slotAnswer[i];
        }

        slotAnswer[at] = same > 0 ? same : top + 1;

        // Глобальный номер - номер САМОГО ответа, по той же причине.
        slotGlobal[at] = Answers;
    }

    // ==== Отправка ====

    private void Dispatch(string payload, bool withFragments)
    {
        if (string.IsNullOrEmpty(payload)) return;

        if (!connectOk) { Fail("Init has not run - call Init first, then Require"); return; }

        fragments = withFragments;

        string problem = ProblemWith(payload);

        if (problem != "") { Fail(problem + ": " + payload); return; }

        int[] symbols = SymbolsOf(payload);

        if (symbols == null) { Fail("character outside the alphabet: " + payload); return; }

        LastError = "";
        forwarded = "";
        forwardedBody = "";

        pendingUrl = payload;
        LastUrl = payload;
        LastSymbols = symbols.Length;
        LastRoad = "";

        if (withFragments) SendCombine(payload); else SendDirect(payload);
    }

    private string PayloadOf(string url)
    {
        if (url.IndexOf(Scheme + "://") == 0) return url.Substring(Scheme.Length + 3);
        //maybe false
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

    private void SendCombine(string payload)
    {
        int[] q = new int[MaxChunks + 1];

        int[] k = q;
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

        int slot = SlotOf(payload);

        int jump = JumpOf(payload);
        bool headed = slot >= 0 && slotHeaded[slot];

        if (jump < 0 && !headed && SendHead(payload)) return;

        LastJump = -1;

        int skip = 0;

        if (jump >= 0 && jump < MaxJumps) skip = count;

        LastRoad = headed
            ? (skip > 0 ? "head/hyper" : "head/combine")
            : (skip > 0 ? "hyper" : "combine");

        queueLen = count - skip + (skip > 0 ? 1 : 0);
        queue = new int[queueLen];
        queueKind = new int[queueLen];

        int at = 0;

        if (skip > 0)
        {
            queue[0] = jump; queueKind[0] = KindJumpOne; at = 1;

            LastJump = jump;
        }

        for (int i = skip; i < count; i++) { queue[at] = q[i]; queueKind[at] = k[i]; at++; }

        SendChain(payload);
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
                if (DirectFragments[f].Length <= pieceLength || at + DirectFragments[f].Length >= payload.Length 
                    || payload.Substring(at, DirectFragments[f].Length) != DirectFragments[f]) continue;

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

        for (int i = 0; i < queueLen; i++) { queue[i] = buffer[i]; queueKind[i] = KindChunk; }

        queue[queueLen - 1] /= DirectPieces;
        queueKind[queueLen - 1] = KindTail;

        SendChain(payload);
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

        // Развод, как у дерева: одинаковые сущности не должны получать одинаковый номер.
        //
        // Вопрос головы - пара «кусок расхождения, найденный номер». Два адреса с общим началом
        // задают ОДИН вопрос, и ссылка им печётся одна и та же, а ответ головы своего адреса не
        // называет и хозяина находит только по ссылке - значит уедет не тому.
        //
        // Развести саму ссылку нечем: свободной осью была бы подпись, но на сервере части кольца
        // идут строго по порядку (stream.Next, Position++), и пропуск одной кладёт поток в 403.
        // Поэтому разводим не ссылку, а вопрос: второй раз он не задаётся, пока первый в полёте,
        // и такой адрес едет обычной сборкой.
        int question = piece * HeadBases + (found % HeadBases);

        for (int i = 0; i < slotKey.Length; i++) if (!slotDelete[i] && slotLoading[i] && slotHead[i] == question) return false;

        int at = SlotOf(payload);

        if (at >= 0) { slotHeaded[at] = true; slotHead[at] = question; }

        LastRoad = "head";
        route = "/hd";

        Load(KindHead, payload, HeadPool[question * JumpSignValues + NextSign()]);

        return true;
    }

    // Добор долга. Занятость его не откладывает: отправку и так выстраивает VRCStringDownloader,
    // а добор лишь забирает у сервера уже готовые тела.
    public void Settle()
    {
        pendingSettle = false;

        LastError = "";
        route = "/tc";

        Load(KindCredit, "", CreditPool[NextSign()]);
    }

    // Отправляет ВСЮ цепочку адреса разом. Куски ложатся в очередь SDK подряд и приходят на сервер
    // в том же порядке, а хвост её закрывает. Ждать ответа на кусок было незачем: в нём нет ничего,
    // что решало бы, каким быть следующему.
    private void SendChain(string payload)
    {
        for (int at = 0; at < queueLen; at++)
        {
            int kind = queueKind[at];
            int value = queue[at];

            // route ставится ДО Load: Load печатает его в Trace, и переставь их местами - в журнале
            // поедет дорога предыдущего шага.
            switch (kind)
            {
                case KindChunk:
                    route = "/c";
                    Load(kind, payload, ChunkPool[value]);
                    continue;

                case KindFragment:
                    route = "/c";
                    Load(kind, payload, VfPool[value]);
                    continue;

                case KindHop:
                    route = "/c";
                    Load(kind, payload, HopPool[value]);
                    continue;

                case KindJumpOne:
                    route = "/h";

                    // Тут адрес ровно один, и просить у сервера восемь значило бы гонять его
                    // наружу за семью чужими. Счётчик 1 - это индекс 0 в блоке номера.
                    Load(KindRange, payload, RangePool[value * RangeMax * JumpSignValues + NextSign()]);
                    continue;

                case KindClose:
                    route = "/cf";
                    Load(KindTail, payload, ClosePool[value * SignValues + NextSign()]);
                    continue;
            }

            // Хвост: отдельной ветки ему не нужно, сюда падает всё, что switch не разобрал.
            //
            // NextSign() в прямой ветке не зовётся, и это важно: части кольца на сервере идут строго
            // по порядку, и лишняя съеденная подпись кладёт поток в 403. Держится на том, что тернарник
            // считает только взятую ветку.
            route = fragments ? "/t" : "/d";

            Load(KindTail, payload, fragments
                ? TailPool[value * SignValues + NextSign()]
                : DirectTailPool[value]);
        }
    }

    private int NextSign()
    {
        if (signs.Length == 0) return 0;

        int sign = signs[signPos] - '0';

        signPos = (signPos + 1) % signs.Length;

        return sign;
    }

    // Рукопожатие идёт строго по одному запросу, поэтому у него своя фаза.
    private void LoadPhase(int nextPhase, VRCUrl url)
    {
        phase = nextPhase;

        route = nextPhase == PhaseConnect ? "/connect" : nextPhase == PhaseCode ? "/k" : "/kf";

        Load(KindCredit, "", url);
    }

    private void Load(int kind, string payload, VRCUrl url)
    {
        LastQueries++;
        TotalQueries++;
        inFlight++;

        lastLoadAt = Time.time;

        if (payload != "")
        {
            int at = SlotOf(payload);

            if (at >= 0)
            {
                slotQueries[at] = slotQueries[at] + 1;

                // Решающий запрос адреса запоминаем: по нему ответ головы найдёт свой слот.
                if (kind == KindHead || kind == KindTail) slotSent[at] = url.Get();
            }
        }

        Trace("request: " + TotalQueries + route, true);

        SendCustomEventDelayedSeconds(nameof(OnLoadTimeout), Timeout);

        VRCStringDownloader.LoadUrl(url, this);
    }

    public void OnLoadTimeout()
    {
        if (inFlight <= 0) return;

        if (Time.time - lastLoadAt < Timeout - 1f) return;

        Fail("no answer in " + Timeout + "s, for " + LastUrl
            + " - url blocked by the SDK or server unreachable");
    }

    // ==== Ответы ====

    public override void OnStringLoadSuccess(IVRCStringDownload response)
    {
        if (inFlight > 0) inFlight--;

        Answers++;

        string json = response.Result;

        // Рукопожатие ведём фазой - оно единственное строго последовательное.
        if (phase == PhaseCode) { phase = PhaseIdle; queuePos++; SendCode(); return; }

        if (phase == PhaseVerify)
        {
            phase = PhaseIdle;

            if (chainInit) { LoadPhase(PhaseConnect, connectReset ? ConnectQuery : RememberQuery); return; }

            return;
        }

        if (phase == PhaseConnect)
        {
            phase = PhaseIdle;
            connectOk = true;

            SeedFromConnect(json);

            return;
        }

        if (!VRCJson.TryDeserializeFromJson(json, out DataToken root) || root.TokenType != TokenType.DataDictionary) return;

        DataDictionary answer = root.DataDictionary;

        // Долг едет довеском к ЛЮБОМУ ответу - забираем до разбора.
        TakeDebt(answer);

        // Чей это ответ, видно по нему самому: прыжок называет адреса, голова находит, хвост закрывает.
        if (answer.ContainsKey("sent")) TakeSent(answer, response.Url.Get());
        else if (answer.ContainsKey("found")) TakeHead(answer, response.Url.Get());
        else if (answer.ContainsKey("leaf")) TakeTail(answer, json);

        Owe();
    }

    // Просит долг: /tc, когда сервер сказал, что у него для нас ещё что-то лежит.
    //
    // Условия ровно два - долг есть и добор ещё не назначен. Третьим стояло "и в полёте пусто", и
    // ради него inFlight здесь и читался. Рассуждение было такое: долг едет довеском к ЛЮБОМУ
    // ответу, а SDK шлёт запросы по одному, так что пока что-то летит, оно привезёт готовое само, и
    // отдельный /tc за ним - лишняя загрузка, лишние пять секунд на ровном месте. Верно ровно до
    // тех пор, пока полёт когда-нибудь пустеет.
    //
    // Он не пустеет. Адрес, которого в дереве нет, идёт сборкой (/c и /t), тело возвращается
    // долгом, риг его не дожидается и через Patience просит заново - а переспрос снова кладёт в
    // полёт куски. Полёт не пустел ни разу, /tc не заводился ни разу, готовое тело так и лежало в
    // ящике: на доске это была "пачка 4: частичная 3/4" при бесконечных vrequest на один адрес.
    //
    // Лишняя загрузка дешевле застрявшей пачки, тем более что прыжковой пачке /tc не нужен вовсе -
    // сервер отдаёт тела прямо ответом на /h. Платит за добор только тот, кто и так пошёл длинной
    // дорогой. inFlight остаётся: занятость наружу и сторож молчания считаются по нему, и только
    // здесь ему не место.
    private void Owe()
    {
        if (LastPending <= 0 || pendingSettle) return;

        pendingSettle = true;

        SendCustomEventDelayedSeconds(nameof(Settle), CreditDelay);
    }

    public override void OnStringLoadError(IVRCStringDownload result)
    {
        if (inFlight > 0) inFlight--;

        // if conditionals up to 3-4, you should mutate you switch
        if (phase == PhaseConnect) { phase = PhaseIdle; connectOk = false; }
        if (phase == PhaseVerify) { phase = PhaseIdle; Fail("codeword rejected"); return; }

        Fail((result.ErrorCode == 0 ? "host unreachable (server not running?), " : "") + result.Error);

        // Упавший запрос долг не привёз - идём за ним сами.
        Owe();
    }

    // Прыжок назвал адреса: у каждого теперь есть номер, и каждому он стоил ровно один запрос.
    private void TakeSent(DataDictionary answer, string link)
    {
        DataDictionary named = new DataDictionary();

        if (answer.TryGetValue("sent", out DataToken list) && list.TokenType == TokenType.DataList)
        {
            DataList sent = list.DataList;

            for (int i = 0; i < sent.Count; i++)
            {
                if (!sent.TryGetValue(i, out DataToken item) || item.TokenType != TokenType.DataDictionary) continue;

                int jump = DictInt(item.DataDictionary, "jump");
                string url = DictString(item.DataDictionary, "url");

                if (url == "" || jump < 0) continue;

                string payload = PayloadOf(url);

                KeepJump(payload, jump);

                named.SetValue(payload, true);

                if (SlotOf(payload) >= 0 && QueriesOf(payload) == 0) Sent(payload, 1);

                Named(url, "vrequest[" + TotalQueries + "] hyper");
            }

            LastUrls = sent.Count;
        }

        // Номер протух: адреса, которые окно должно было накрыть, просим заново - сборкой.
        if (!DictBool(answer, "known")) { Reopen(link); return; }

        // Кого окно накрыло - решил сервер, и вот его ответ. Кого не назвал, отпускаем сразу:
        // ждать по ним нечего, а следующий проход даст им своё окно.
        int at = -1, smallest = 0;
        bool left = false, ours = false;

        for (int i = 0; i < slotKey.Length; i++)
        {
            if (slotDelete[i] || slotSent[i] != link) continue;

            int jump = JumpOf(slotKey[i]);

            if (jump >= 0 && (at < 0 || jump < smallest)) { at = i; smallest = jump; }

            if (named.ContainsKey(slotKey[i])) { ours = true; continue; }

            Flying(i, false);
            slotSent[i] = "";
            left = true;
        }

        if (!left) return;

        // Окно не назвало даже собственный якорь: его номер бесполезен, и без этого следующий
        // проход выбрал бы тот же якорь и встал. Такой адрес едет сборкой.
        if (!ours && at >= 0) jumps.Remove(slotKey[at]);

        Jumps();
        Spell();
    }

    // Голова нашла адреса по куску расхождения. Свой слот находим по печёной ссылке: в ответе его нет.
    private void TakeHead(DataDictionary answer, string link)
    {
        int at = SlotBySent(link);
        string payload = at < 0 ? "" : slotKey[at];

        if (answer.TryGetValue("found", out DataToken list) && list.TokenType == TokenType.DataList)
        {
            DataList found = list.DataList;

            for (int i = 0; i < found.Count; i++)
            {
                if (!found.TryGetValue(i, out DataToken item) || item.TokenType != TokenType.DataDictionary) continue;

                string url = DictString(item.DataDictionary, "url");
                int jump = DictInt(item.DataDictionary, "jump");

                if (url == "" || jump < 0) continue;

                KeepJump(PayloadOf(url), jump);

                Named(url, "head[" + TotalQueries + "]");
            }

            LastUrls = found.Count;
        }

        if (payload == "" || Loaded(payload)) return;

        // Наш адрес голова назвала - дальше он уедет прыжком. Не назвала - диктуем остаток сами.
        if (JumpOf(payload) >= 0) { SendCombine(payload); return; }

        string kept = DictString(answer, "kept");

        SendCombine(kept == "" ? payload : payload.Substring(0, payload.Length - kept.Length));
    }

    // Хвост закрыл адрес. Какой именно - сказано в самом ответе, поэтому помнить «текущий» не нужно.
    private void TakeTail(DataDictionary answer, string json)
    {
        string payload = PayloadOf(DictString(answer, "forwardedUrl"));
        int leaf = DictInt(answer, "leaf");

        if (payload != "" && leaf >= 0) KeepJump(payload, leaf);

        LastChunks = DictInt(answer, "chunks");
        LastL2 = DictInt(answer, "l2");
        LastL3 = DictInt(answer, "l3");
        LastInfinite = DictInt(answer, "infinite");
        LastUrls = 1;

        LearnFragmentList(answer);

        forwarded = json;

        if (payload != "") Named(payload, "vrequest[" + TotalQueries + "] " + LastRoad);
    }

    private void TakeDebt(DataDictionary answer)
    {
        // Старое кольцо частей кончилось - сервер прислал новое тем же ответом, в котором мы
        // потратили последнюю часть. Ту часть он уже зачёл, поэтому новое кольцо начинаем с начала.
        string fresh = DictString(answer, "signs");

        if (fresh != "" && fresh != signs) { signs = fresh; signPos = 0; }

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

            KeepBody(url, StatusLoaded, body);

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

    private void RememberChain(int leaf)
    {
        if (leaf < 0 || pendingUrl == "") return;

        KeepJump(pendingUrl, leaf);
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

    private int JumpOf(string url)
    {
        if (!jumps.TryGetValue(url, out DataToken value)) return -1;

        return value.TokenType == TokenType.Int ? value.Int : -1;
    }

    // ==== Лог ====

    private int OpenRequest(DataList urls)
    {
        if (urls.Count == 0) return -1;

        int id = ++vrequests;

        DataList settled = new DataList();

        for (int i = 0; i < urls.Count; i++) settled.Add(false);

        DataList record = new DataList();

        // Дороги у пачки ещё нет: OpenRequest зовётся ДО первой отправки, и route здесь - дорога
        // предыдущей пачки. Из-за этого в журнале стояло "vrequest: 2/tc" у пачки, которая ушла
        // прыжками. Вместо вранья кладём метку просящего - она-то как раз известна.
        string mark = who == "" ? "" : "/" + who;

        record.Add(mark);
        record.Add(urls);
        record.Add(settled);

        // Часы vrequest идут с ЭТОГО мгновения. Было lastLoadAt - время ПРЕДЫДУЩЕГО запроса, и
        // пачка наследовала чужой отсчёт: между нажатиями мир стоит сколько угодно, и в журнал
        // уезжали десятки секунд простоя вместо настоящих секунд работы.
        record.Add(Time.time);

        record.Add("");
        record.Add("");
        record.Add(0);

        openRequests.SetValue(id, record);

        for (int i = 0; i < urls.Count; i++)
            if (urls.TryGetValue(i, out DataToken url) && url.TokenType == TokenType.String)
                asking.SetValue(url.String, id);

        Trace("vrequest: " + id + mark + " " + Joined(urls), true);

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

    private void Trace(string line, bool request)
    {
        if (request) outgoing = outgoing == "" ? line : outgoing + "\n" + line;
        else incoming = incoming == "" ? line : incoming + "\n" + line;

        Debug.Log("[CombineQueries] " + line);
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

    // ==== JSON ====

    private bool DictBool(DataDictionary dict, string field)
    {
        if (!dict.TryGetValue(field, out DataToken value)) return false;

        return value.TokenType == TokenType.Boolean && value.Boolean;
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

    // ==== Сборка пулов ====

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
}
