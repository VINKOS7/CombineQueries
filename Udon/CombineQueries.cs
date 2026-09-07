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

    // Сколько узлов цепочек умеем адресовать прыжком. Пул печётся, как и всё остальное.
    private const int MaxJumps = 4096;

    // Размер Развязки-1: сколько слотов печём и сообщаем серверу в connect. Меняешь размер - правишь обе константы (число и строку).
    private const int dfaSize = 1024;
    private const string DfaSizeStr = "1024";

    // Страницы Развязки-2 (L3). Глобальный адрес VF = page*dfaSize + offset, ёмкость = dfaSize*pageCount.
    // page едет прямо в URL, поэтому L3 стоит ОДИН запрос (а не /g/ + /f/), но ценой VF-пула в
    // dfaSize*pageCount ссылок - при двух запросах печаталось бы всего dfaSize+pageCount.
    // Отсюда и потолок: 1024*64 = 65 536 ссылок, ~8% сверх чанк-пула 94^3 = 830 584.
    // Меняешь размер - правишь обе константы (число и строку).
    private const int pageCount = 64;
    private const string PageCountStr = "64";

    // Развязка-3 (Infinite): сколько ёмкостей умеем перепрыгнуть одним доп. запросом. Адресуемо
    // всего dfaSize*pageCount*hopCount = 4 194 304, а печём под это лишь hopCount ссылок.
    // 1 = развязки нет, за L3 ничего не адресуется.
    private const int hopCount = 64;
    private const string HopCountStr = "64";

    // Свитч в бесконечность: тянуть ли в свой словарь строки за потолком L3.
    //
    // false - потолок кеша L2+L3 (65 536 строк, единицы мегабайт). Адресовать Infinite мы всё равно
    // умеем (якорь + hop), просто не возим весь хвост: строка уходит туда именно потому, что
    // встречается реже всех, и дешевле подтянуть её пиггибэком по факту.
    // true - сервер шлёт словарь целиком, до 4 млн строк. Это сотни мегабайт, Udon столько не держит.
    private const bool rememberInfinite = true;
    private const string RememberInfiniteStr = "true";

    // Подпись хвоста: сколько у неё значений. Каждый /t/ несёт очередную из выданной на connect
    // последовательности, сервер сверяет - не совпало, приём валится до повторного connect.
    //
    // Живёт в хвосте, а не в чанках: у /t/ свой маленький пул (8931), подпись множит только его -
    // 71 448 ссылок, +5,6% к общему. В /c/ то же самое стоило бы 830 584 * SignValues.
    //
    // ВЫКЛЮЧАТЕЛЬ: 1 - подпись единственная, пул не растёт, сверка тривиальна. Вырезать код не надо.
    private const int SignValues = 8;

    // Подпись прыжка - та же и полная, что у хвоста: прыжок САМ отдаёт собранный адрес наружу,
    // значит по правам он равен хвосту, а не куску. Кольцо общее - /h/ и /t/ съедают по позиции.
    //
    // Цена: пул прыжков множится на SignValues (4096 -> 32768 ссылок, ~3 МБ). Это плата за то,
    // что хайпер укладывается в ОДИН запрос вместо двух.
    private const int JumpSignValues = SignValues;

    // Сбрасывать ли хайперы при инициализации карты. Собранный однажды url дальше уходит одним
    // запросом /h/, и повторный прогон теста меряет уже не сборку - без сброса второй заход
    // бессмыслен.
    //
    // Едет параметром в самом connect, а не отдельным запросом: инициализация и есть connect,
    // а лишний round-trip тут стоит дороже всего. Сервер уважает флаг ТОЛЬКО в dev.
    //
    // Не путать с hypers=on|off: тот решает, попадают ли НОВЫЕ цепочки в БД (накопленное читается
    // в любом случае), а этот - стереть ли хайперы, накопленные в памяти сервера.
#if CQ_RELEASE
    // В релизе сброса не просим вовсе: сервер уважает флаг только в dev, а мир, куда зашли игроки,
    // забывать накопленное не должен ни при каких настройках сервера.
    private const bool resetHypers = false;
    private const string ResetHypersStr = "false";
#else
    private const bool resetHypers = true;
    private const string ResetHypersStr = "true";
#endif

    // Копить ли НОВЫЕ цепочки в БД сервера. Задаёт мод сборки: dev - "off" (гиперизация живёт в
    // ОЗУ сервера, в базе только посев из миграции), release - "on". Накопленное читается всегда,
    // флаг гасит только рост.
    private const string GrowHypersStr = CombineQueriesEnvironment.Hypers;

    // Роут combine: /c/{runes}/{id}/{page}/{hop}/{q}. Чанк - q=0 (остальное нули), VF - q=1
    // (руна-сентинел, реальные offset/page), Развязка-3 - hop>0. Хвост, хайпер и код своими роутами.
    private readonly VRCUrl[] ChunkPool = PoolOf(baseUrl + "/c/", "/0/0/0/0", Symbols, RuneAlphabet, RuneSize, RuneWidth);
    private readonly VRCUrl[] TailPool = TailPoolOf(baseUrl + "/t/", Symbols, RuneAlphabet, RuneSize, RuneWidth, SignValues);
    private readonly VRCUrl[] DirectTailPool = DirectTailPoolOf(baseUrl + "/d/", 59, RuneAlphabet, RuneSize, RuneWidth);
    private readonly VRCUrl[] VfPool = VfPoolOf(baseUrl + "/c/", RuneAlphabet, RuneWidth, dfaSize, pageCount);

    // Развязка-3 (Infinite): сдвиг адреса на hop ёмкостей. Печём hopCount ссылок - не пул на каждый
    // адрес, а один разряд поверх VF-пула, поэтому адресуемое пространство растёт в hopCount раз
    // ценой всего hopCount ссылок.
    private readonly VRCUrl[] HopPool = HopPoolOf(baseUrl + "/c/", RuneAlphabet, RuneWidth, hopCount);
    // Прыжок в точку ранее пройденной combine-цепочки: /h/<узел>.
    private readonly VRCUrl[] JumpPool = NumPoolOf(baseUrl + "/h/", MaxJumps, JumpSignValues);
    private readonly VRCUrl[] AuthPool = AuthPoolOf(baseUrl + "/k/", AuthAlphabet);
    private readonly VRCUrl VerifyQuery = new VRCUrl(baseUrl + "/kf");

    private readonly VRCUrl ConnectQuery = new VRCUrl(baseUrl + "/connect?alphabet=" + AlphabetEncoded + "&baseQuery=" + baseForwardUrl + "&runeSize=" + RuneSizeStr + "&scheme=" + Scheme + "&token=" + Token + "&dfaSize=" + DfaSizeStr + "&pageCount=" + PageCountStr + "&hopCount=" + HopCountStr + "&rememberInfinite=" + RememberInfiniteStr + "&resetHypers=" + ResetHypersStr + "&hypers=" + GrowHypersStr);

    // Тот же connect, но БЕЗ сброса и С ПЕРСИСТОМ: Remember по смыслу возвращает накопленное, и
    // сбрасывать хайперы тем же движением - значит стирать ровно то, за чем шли.
    //
    // Отличается ровно сбросом: рост цепочек в БД задаёт GrowHypersStr, и он одинаков у обоих -
    // это свойство сборки, а не кнопки. Стоит одну печёную ссылку.
    private readonly VRCUrl RememberQuery = new VRCUrl(baseUrl + "/connect?alphabet=" + AlphabetEncoded + "&baseQuery=" + baseForwardUrl + "&runeSize=" + RuneSizeStr + "&scheme=" + Scheme + "&token=" + Token + "&dfaSize=" + DfaSizeStr + "&pageCount=" + PageCountStr + "&hopCount=" + HopCountStr + "&rememberInfinite=" + RememberInfiniteStr + "&resetHypers=false&hypers=" + GrowHypersStr);

    [Header("Where to report completion (optional)")]
    public UdonSharpBehaviour target;
    public string onDoneEvent = "OnQueryDone";

    [Header("Codeword typed in-world before Init/Remember")]
    public string codeword = "";

    public string LastError = "";
    public string LastUrl = "";
    public int LastSymbols;
    public int LastQueries;

    // Покрытие последнего url: сколько кусков ушло рунами и сколько фрагментами по уровням.
    // Partial это LastChunks > 0 - словаря не хватило, часть поехала по буквам.
    public int LastChunks;
    public int LastL2;
    public int LastL3;
    public int LastInfinite;

    // Номер прыжка, которым ушёл последний адрес, или -1 - собирали с нуля. Нужен снаружи:
    // у короткой цепочки прыжок не меняет ЧИСЛО запросов, и по счётчику его не видно.
    public int LastJump = -1;

    // Сколько прыжков приехало сидом в connect: это ровно то, что сервер помнит из БД.
    // При resetHypers=true всегда 0 - dev-сброс стирает дерево вместе с хайперами.
    public int SeedJumps;

    private const int PhaseIdle = 0;
    private const int PhaseConnect = 1;
    private const int PhaseChunks = 2;
    private const int PhaseTail = 3;
    private const int PhaseCode = 5;
    private const int PhaseVerify = 6;
    private const int PhaseFragment = 7;
    private const int PhaseJump = 8;

    private int phase;
    private bool connectOk;
    private bool busy;
    private bool chainInit;

    private int[] queue;
    private int[] queueKind;
    private int queueLen;
    private int queuePos;
    private bool fragments = true;

    private string pendingUrl = "";
    private string forwarded = "";

    // Последовательность подписей с connect. Позиции обязаны идти в ногу с серверными: разъедутся -
    // сервер посчитает хвост чужим и свалит приём.
    private string signs = "";
    private int signPos;

    // ХАЙПЕР = кеш «где лежит уже использованная цепочка COMBINE-запросов».
    //
    // Цепочка - это ровно последовательность /c/: чанки и фрагменты, в порядке отправки. Хвост в
    // неё не входит, он лишь закрывает сборку; поэтому прыжок восстанавливает combine-часть, а
    // хвост потом идёт как обычно.
    //
    // Дерево живёт на СЕРВЕРЕ, клиент держит от него только плоский словарь «адрес -> номер узла».
    // Так дешевле: у фронта тут нет ни спуска по шагам, ни трёх массивов с линейным поиском
    // ребёнка, а поиск прыжка становится одним TryGetValue.
    //
    // Платим за это прыжками с ОБЩЕГО НАЧАЛА: похожий адрес (products/12/reviews после
    // products/12/comments) снова соберётся целиком. Целые повторы - основной случай - экономятся.
    //
    // Номер называет сервер (leaf в ответе хвоста) и он переживает рестарт: дерево лежит в БД, а
    // словарь приезжает сидом в connect.
    private DataDictionary jumps = new DataDictionary();

    // Словарь динамических фрагментов, зеркало серверного: адрес -> подстрока.
    // Заполняется сидом из connect и пиггибэком из /t/.
    private string[] cachedFragments = new string[0];
    private int[] cachedFragIds = new int[0];

    // Полное подключение: локальный словарь выбрасываем и берём серверный сид целиком.
    // Имя совпадает с эндпоинтом: в сцене событие так и зовётся - Connect.
    public void Connect()
    {
        if (busy) return;

        LastError = "";

        if (RuneAlphabet.Length != Alphabet.Length - 6) { Fail("RuneAlphabet must be Alphabet minus #%[]/?"); return; }

        roots = new string[0];
        cachedFragments = new string[0];
        cachedFragIds = new int[0];
        jumps = new DataDictionary();

        Begin(true);
    }

    // Забыть СВОИ прыжки, не трогая серверные. Нужно, чтобы увидеть персист как его видит новый
    // игрок: клиент про адрес не знает ничего, а сервер отдаёт его прыжок сидом в connect.
    public void ForgetJumps() => jumps = new DataDictionary();

    // Повторное подключение ДЕЛЬТОЙ: всё, что уже знаем, оставляем при себе - сервер дошлёт
    // недостающее тем же сидом. Нужно после гашения хоста или реконнекта.
    //
    // Раньше выходило молча при RequireCode=false и до connect не доходило вовсе, то есть
    // восстановление словаря не работало в принципе - а именно ради него метод и нужен.
    public void Remember()
    {
        if (busy) return;

        LastError = "";

        Begin(false);
    }

    // Оба пути ведут в connect: без кодового слова напрямую, с ним - после набора.
    // Первое это подключение или повторное, решает сервер: у него виден уже стоящий контекст.
    private void Begin(bool reset)
    {
        connectReset = reset;

        if (!RequireCode) { busy = true; Load(PhaseConnect, reset ? ConnectQuery : RememberQuery); return; }

        StartCode(true);
    }

    // Каким connect закрывать набор кода: полным (со сбросом) или Remember.
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

    public void Request(string url) => Send(url, true);

    public void RequestDirect(string url) => Send(url, false);

    private void Send(string url, bool withFragments)
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
        pendingUrl = payload;
        LastUrl = url;
        LastSymbols = symbols.Length;
        LastQueries = 0;
        busy = true;

        if (withFragments) SendCombine(payload); else SendDirect(payload);
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

                char here = payload[pos];

                for (int i = 0; i < cachedFragments.Length; i++)
                {
                    if (cachedFragments[i].Length <= flen || pos + cachedFragments[i].Length > payload.Length) continue;

                    // Отсечка по первому символу до Substring: она дешёвая, а Substring в Udon дорог
                    // и на словаре в 1024 строки звался бы на каждой позиции URL.
                    if (cachedFragments[i][0] != here) continue;

                    if (payload.Substring(pos, cachedFragments[i].Length) != cachedFragments[i]) continue;

                    fid = cachedFragIds[i];
                    flen = cachedFragments[i].Length;
                }

                if (fid >= 0)
                {
                    // Финитный адрес (< ёмкости) шлём одним запросом. Бесконечный своего печёного
                    // адреса не имеет и занимает его у финитной строки: младший разряд (якорь) едет
                    // обычным VF, старший - Развязкой-3, то есть плюс ОДИН запрос, не больше.
                    int capacity = dfaSize * pageCount;
                    int hop = fid / capacity;
                    int anchor = fid - hop * capacity;

                    // Столько чанков стоила бы эта подстрока буквами. Если адресация дороже - не
                    // берём фрагмент вовсе: это graceful degradation хвоста.
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

        q[count] = tail; k[count] = 2; count++;

        // Уже собранный адрес заменяем ОДНИМ прыжком: сервер поднимает всю combine-часть сам, а мы
        // досылаем только хвост. Столько запросов и экономится - все, кроме двух.
        int jump = JumpOf(payload);

        LastJump = -1;

        int skip = 0;

        // Прыжок закрывает адрес ЦЕЛИКОМ - сервер знает и хвост, поэтому форвардит сам.
        // Правило простое: хайпер это один запрос. Не попали в словарь - идём обычной дорогой,
        // combine + tail по динамической фрагментации.
        if (jump >= 0 && jump < MaxJumps) skip = count;

        queueLen = count - skip + (skip > 0 ? 1 : 0);
        queue = new int[queueLen];
        queueKind = new int[queueLen];

        int at = 0;

        if (skip > 0)
        {
            queue[0] = jump; queueKind[0] = 4; at = 1;

            LastJump = jump;

#if !CQ_RELEASE
            Debug.Log("[CombineQueries] hyper: jump " + jump + " replaces all " + skip + " queries with one");
#endif
        }

        for (int i = skip; i < count; i++) { queue[at] = q[i]; queueKind[at] = k[i]; at++; }

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
        LastJump = -1;

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

        if (kind == 1) { Load(PhaseFragment, VfPool[queue[queuePos]]); return; }

        // Развязка-3: старший разряд бесконечного адреса, сдвиг делает сервер.
        if (kind == 3) { Load(PhaseFragment, HopPool[queue[queuePos]]); return; }

        // Прыжок в известную точку combine-цепочки. Подпись берём из общего кольца - её позицию
        // сервер сдвинет тем же движением, что и на хвосте.
        if (kind == 4) { Load(PhaseJump, JumpPool[queue[queuePos] * JumpSignValues + NextSign() % JumpSignValues]); return; }

        // Direct подписи не несёт: сервер сверяет её только на fragmentate-хвосте.
        if (!fragments) { Load(PhaseTail, DirectTailPool[queue[queuePos]]); return; }

        Load(PhaseTail, TailPool[queue[queuePos] * SignValues + NextSign()]);
    }

    // Очередная подпись кольца. Позицию двигают ОБА потребителя - и хвост, и прыжок.
    private int NextSign()
    {
        if (signs.Length == 0) return 0;

        int sign = signs[signPos] - '0';

        signPos = (signPos + 1) % signs.Length;

        return sign;
    }

    public override void OnStringLoadSuccess(IVRCStringDownload response)
    {
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

            // Логи по обе стороны разбора: если виден только первый, значит упали в SeedFromConnect,
            // и молчание клиента - это не «ответ не пришёл», а исключение внутри Udon.
            Debug.Log("[CombineQueries] connect: answer " + response.Result.Length + " bytes");

            SeedFromConnect(response.Result);

            // В релизе про хайперы молчим: сколько их приехало и когда сработал прыжок - это
            // внутренняя кухня, по ней видно «до и после персиста», а игроку она ни к чему.
#if CQ_RELEASE
            Debug.Log("[CombineQueries] connect: ready, roots " + roots.Length + ", fragments " + cachedFragments.Length);
#else
            Debug.Log("[CombineQueries] connect: ready, roots " + roots.Length + ", fragments " + cachedFragments.Length + ", jumps " + jumps.Count);
#endif

            Done();
            return;
        }

        if (phase == PhaseChunks || phase == PhaseFragment) { queuePos++; SendNext(); return; }

        // Прыжок отдаёт адрес и тело сразу - это и есть весь запрос.
        if (phase == PhaseJump)
        {
            // Сервер такого узла не знает (например, базу почистили): забываем прыжок и идём
            // обычной дорогой - собираем адрес с нуля.
            if (!BoolField(response.Result, "known"))
            {
                Debug.Log("[CombineQueries] hyper: jump " + LastJump + " unknown on server ("
                    + StringField(response.Result, "note") + "), assembling instead");

                jumps.Remove(pendingUrl);

                LastJump = -1;

                SendCombine(pendingUrl);
                return;
            }

            LastChunks = 0;
            LastL2 = 0;
            LastL3 = 0;
            LastInfinite = 0;

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

            forwarded = response.Result;

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

    // Секунд ожидания ответа. VRChat отбивает загрузку молча - ни успеха, ни ошибки, - и тогда
    // busy остаётся поднятым навсегда: клиент внешне «висит», а все следующие нажатия молча
    // выходят через if (busy). Сторож превращает это в понятную ошибку.
    //
    // Порог с запасом: SDK САМ откладывает старт загрузки, выдерживая свой интервал между ними
    // (в трассе это видно как StartAtCorrectTime). Мы же считаем время от вызова LoadUrl, то есть
    // ждём и очередь SDK тоже. На 20 с сторож срабатывал вхолостую на живых запросах.
    private const float Timeout = 60f;

    private float lastLoadAt;

    private void Load(int nextPhase, VRCUrl url)
    {
        phase = nextPhase;

        LastQueries++;
        lastLoadAt = Time.time;

        SendCustomEventDelayedSeconds(nameof(OnLoadTimeout), Timeout);

        VRCStringDownloader.LoadUrl(url, this);
    }

    // Зовётся Udon по таймеру. Сторож ставится на КАЖДУЮ загрузку, поэтому старые срабатывают и
    // после того, как их запрос давно закрыт. Смотрим на время последней загрузки, а не на счётчик:
    // LastQueries считает запросы на ОДИН url и сбрасывается на каждом новом, так что сравнение
    // номеров ловило ложные совпадения и убивало живые запросы.
    public void OnLoadTimeout()
    {
        if (!busy) return;

        if (Time.time - lastLoadAt < Timeout - 1f) return;

        Fail("no answer in " + Timeout + "s on phase " + phase + ", query " + LastQueries
            + " of " + queueLen + " for " + LastUrl + " - url blocked by the SDK or server unreachable");
    }

    // Номер узла, который назвал сервер: сюда можно прыгнуть в следующий раз за этим же адресом.
    private void RememberChain(int leaf)
    {
        if (leaf < 0 || pendingUrl == "") return;

        jumps.SetValue(pendingUrl, leaf);
    }

    private int JumpOf(string url)
    {
        if (!jumps.TryGetValue(url, out DataToken value))
        {
#if !CQ_RELEASE
            if (jumps.Count > 0) Debug.Log("[CombineQueries] no jump for '" + url + "' (known " + jumps.Count + ")");
#endif
            return -1;
        }

        return value.TokenType == TokenType.Int ? value.Int : -1;
    }

    private void Done()
    {
        busy = false;
        phase = PhaseIdle;

        // Без target клиент отработает молча, и снаружи это неотличимо от зависания - логируем сами.
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

        Debug.LogError("CombineQueries: " + reason);

        Done();
    }

    private void SeedFromConnect(string json)
    {
        if (!VRCJson.TryDeserializeFromJson(json, out DataToken root)) return;
        if (root.TokenType != TokenType.DataDictionary) return;

        DataDictionary dict = root.DataDictionary;

        SeedJumps = 0;

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

        // Подписи хвоста. Позицию сбрасываем вместе с ними: сервер на connect рождает новую
        // последовательность и начинает с нуля.
        signs = DictString(dict, "signs");
        signPos = 0;

        SeedJumpList(dict);
        LearnFragmentList(dict);
    }

    // Хайперы из персиста: адрес -> номер прыжка. Дерево осталось на сервере, сюда приезжают
    // только его листы, поэтому уже собранный кем-то адрес идёт двумя запросами с первого раза.
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

            jumps.SetValue(url, jump);
            SeedJumps++;

            // Ключ словаря - адрес БЕЗ схемы, ровно в том виде, в каком его собирает сервер.
            // Если прыжок не срабатывает, расходятся обычно именно ключи - печатаем первый.
#if !CQ_RELEASE
            if (SeedJumps == 1) Debug.Log("[CombineQueries] seed jump: '" + url + "' -> " + jump);
#endif
        }
    }

    // Новые фрагменты из ответа /t/ (пиггибэк): та же таблица, что и в сиде.
    private void LearnFragments(string json)
    {
        if (!VRCJson.TryDeserializeFromJson(json, out DataToken root)) return;
        if (root.TokenType != TokenType.DataDictionary) return;

        LearnFragmentList(root.DataDictionary);
    }

    // Пакетная заливка: массив растёт ОДИН раз на весь список, а не на каждый фрагмент.
    // Поштучный рост давал квадрат по копированию - на сиде в 1024 строки это
    // около миллиона копирований в интерпретаторе, и init висел вместо того, чтобы отработать.
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

            // Верхней границы нет намеренно: dfaSize отсекал бы L3 (адреса от dfaSize до ёмкости),
            // а всё, что выше ёмкости - Infinite, оно адресуется якорем и хопами. Годится любой id.
            if (id < 0 || text == "") continue;

            // Сверяемся только со СТАРЫМИ адресами: внутри одного ответа сервер не шлёт дублей.
            bool known = false;

            for (int j = 0; j < have; j++) if (ids[j] == id) { known = true; break; }

            if (known) continue;

            texts[n] = text;
            ids[n] = id;
            n++;
        }

        if (n == texts.Length) { cachedFragments = texts; cachedFragIds = ids; return; }

        // Что-то отсеялось - ужимаем до фактического размера, но однократно.
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

    // VF-пул объединённого роута: /c/<руна-сентинел>/<offset>/<page>/1. Индекс = ГЛОБАЛЬНЫЙ id
    // (page*slots + offset), поэтому и L2 (page=0), и L3 адресуются одним VfPool[id].
    // Руна тут сентинел: при q=1 сервер её игнорирует и берёт VF по id.
    private static VRCUrl[] VfPoolOf(string baseUri, string runeAlph, int runeWidth, int slots, int pages)
    {
        string sentinel = RunesOf(0, runeAlph, runeWidth);

        VRCUrl[] pool = new VRCUrl[slots * pages];

        for (int p = 0; p < pages; p++)
            for (int o = 0; o < slots; o++)
                pool[p * slots + o] = new VRCUrl(baseUri + sentinel + "/" + o + "/" + p + "/0/1");

        return pool;
    }

    // Развязка-3: сегмент hop сдвигает последний принятый VF на hop ёмкостей. Руны здесь сентинел,
    // id и page не значат ничего - весь смысл в одном сегменте, оттого и пул всего в hopCount ссылок.
    // Индекс 0 не используется (0 = обычный запрос), но место держим, чтобы индекс = самому сдвигу.
    private static VRCUrl[] HopPoolOf(string baseUri, string runeAlph, int runeWidth, int hops)
    {
        string sentinel = RunesOf(0, runeAlph, runeWidth);

        VRCUrl[] pool = new VRCUrl[hops];

        for (int h = 0; h < hops; h++) pool[h] = new VRCUrl(baseUri + sentinel + "/0/0/" + h + "/1");

        return pool;
    }

    // Индекс = хвост * signs + подпись. Печём каждое сочетание: подпись обязана быть в самом URL,
    // добавить её в рантайме нельзя - VRCUrl запечён целиком.
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

    // Индекс = номер * signs + подпись. Как и у хвоста, подпись обязана быть в самом URL:
    // печётся каждое сочетание, иначе её нельзя было бы поставить в рантайме.
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
