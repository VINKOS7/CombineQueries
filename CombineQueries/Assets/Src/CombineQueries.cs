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

    // Голова потока: две развязки в одном запросе.
    //
    // Первая - кусок combine: то, чем искомый адрес ОТЛИЧАЕТСЯ от похожего. Вторая - обрезок номера
    // самого похожего (база): целиком номер не влезает, да и не нужен - вторую половину работы
    // делает кусок. В этом и экономия: гипер едет не весь.
    //
    // Сервер складывает одно с другим и получает ТОЧНЫЙ адрес, поэтому отвечает готовым телом.
    //
    // Разрядность делим в пользу КУСКА, и вот почему. База - обрезок, а не адрес: любой номер
    // кольца обрезается и уезжает, непокрытых нет. Её ширина решает лишь точность, а промах по
    // точности стоит дёшево - сервер назовёт лишних соседей, и клиент положит их номера себе
    // даром. Кусок же упирается насмерть: фрагмент с адресом за потолком голова назвать не может
    // вообще, а в словаре такие есть (тот же comments/post/1 живёт за тысячей).
    //
    // Отсюда 2048 x 8, а не 1024 x 16: пул тот же (131 072 ссылки, ~13 МБ), кусок достаёт вдвое
    // дальше, база грубеет на бит. Отдельная цифра страницы для этого не нужна - это та же
    // разрядность, только записанная двумя числами; она понадобится, лишь если растить память.
    private const int HeadLimit = 2048;
    private const int HeadBases = 8;

    // Подпись прыжка - та же и полная, что у хвоста: прыжок САМ отдаёт собранный адрес наружу,
    // значит по правам он равен хвосту, а не куску. Кольцо общее - /h/ и /t/ съедают по позиции.
    //
    // Цена: пул прыжков множится на SignValues (4096 -> 32768 ссылок, ~3 МБ). Это плата за то,
    // что хайпер укладывается в ОДИН запрос вместо двух.
    private const int JumpSignValues = SignValues;

    // Пачка ДИАПАЗОНОМ: сколько адресов забираем одним запросом.
    //
    // Перечислить номера в адресе нельзя - печётся каждое сочетание, два произвольных дают 4096^2
    // ссылок. Диапазон растёт линейно: 4096 * RangeMax * подписи. Работает потому, что номера
    // выдаются ПО ПОРЯДКУ - адреса, собранные подряд, лежат рядом.
    //
    // Четыре, а не восемь: серии длиннее четырёх бывают только когда адреса собрали подряд в одну
    // сессию, а 4 стоят ровно столько же, сколько стоила пара - 131 072 ссылки.
    private const int RangeMax = 4;

    // Докуда достаёт закрывающая форма /cf. Кусок с адресом ниже этого можно продиктовать и
    // закрыть одним запросом; выше - обычной парой «кусок + хвост».
    private const int CloseLimit = 1024;

    // Сбрасывать ли хайперы при инициализации карты. Собранный однажды url дальше уходит одним
    // запросом /h/, и повторный прогон теста меряет уже не сборку - без сброса второй заход
    // бессмыслен.
    //
    // Едет параметром в самом connect, а не отдельным запросом: инициализация и есть connect,
    // а лишний round-trip тут стоит дороже всего. Сервер уважает флаг ТОЛЬКО в dev.
    //
    // Не путать с hypers=on|off: тот решает, попадают ли НОВЫЕ цепочки в БД (накопленное читается
    // в любом случае), а этот - стереть ли хайперы, накопленные в памяти сервера.
#if CQ_PROD
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
    // Голова: /hd/<адрес фрагмента>/<подпись>. Спрашиваем по СОДЕРЖИМОМУ - номера мы не знаем.
    private readonly VRCUrl[] HeadPool = HeadPoolOf(baseUrl + "/hd/", HeadLimit, HeadBases, JumpSignValues);
    // Пачка прыжков одним запросом: /h/<первый>/<сколько>/<подпись>.
    private readonly VRCUrl[] RangePool = RangePoolOf(baseUrl + "/h/", MaxJumps, RangeMax, JumpSignValues);
    // Погашение долга: /tc/<подпись>. Отдельный эндпоинт, а не диапазон нулевой длины: у «забери
    // долг» нет ни номера, ни количества, и притворяться прыжком ему незачем. Стоит 8 ссылок.
    private readonly VRCUrl[] CreditPool = CreditPoolOf(baseUrl + "/tc/", JumpSignValues);

    // Кусок и закрытие одним запросом: /cf/<адрес куска>/<подпись>. Адрес, целиком накрытый одним
    // куском словаря, стоил два запроса - продиктовать и закрыть; теперь один.
    //
    // Только младшие CloseLimit кусков: на весь словарь (1024 * 64 * 8) это было бы полмиллиона
    // ссылок, а так - 8192, меньше мегабайта. Частые короткие начала все лежат в младших адресах.
    private readonly VRCUrl[] ClosePool = ClosePoolOf(baseUrl + "/cf/", CloseLimit, SignValues);
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

    // Сколько отказов было всего. Нужен снаружи: по тексту ошибки повтор не отличить от старого -
    // сервер лежит, и вторая попытка падает ДОСЛОВНО тем же сообщением. Риг, сравнивавший строки,
    // такой отказ не замечал и оставался ждать тело, которое уже не приедет.
    public int Errors;

    // Покрытие последнего url: сколько кусков ушло рунами и сколько фрагментами по уровням.
    // Partial это LastChunks > 0 - словаря не хватило, часть поехала по буквам.
    public int LastChunks;
    public int LastL2;
    public int LastL3;
    public int LastInfinite;

    // Сколько адресов затребовал последний запрос. У сборки это всегда 1, у прыжка - столько,
    // сколько он покрыл диапазоном, у головы - сколько она нашла. Тела при этом приезжают долгом,
    // так что число говорит «за сколькими пошли», а не «сколько уже на руках».
    public int LastUrls;

    // Номер прыжка, которым ушёл последний адрес, или -1 - собирали с нуля. Нужен снаружи:
    // у короткой цепочки прыжок не меняет ЧИСЛО запросов, и по счётчику его не видно.
    public int LastJump = -1;

    // Концы адресов, которые уехали ПОСЛЕДНИМ запросом, через запятую. Один запрос давно значит не
    // один адрес: диапазон тащит четвёрку, голова называет до восьми, - и по строке «отправлено»
    // это единственное место, где видно, за чем именно ходили.
    public string LastSent = "";

    // Дорога последнего адреса. Одного счётчика запросов мало: голова стоит запрос и НЕ говорит,
    // чем кончилось - назвала адрес (дальше один прыжок) или не знала его (дальше вся сборка).
    // Различаем именно это:
    //   combine       - собрали с нуля, голову спросить было нечем: расхождение не легло на
    //                   фрагмент, либо в кольце нет базы с тем же началом. Чем полнее кольцо, тем
    //                   реже эта дорога встречается - в пределе её быть не должно вовсе
    //   hyper         - прыжок из кольца, головы не понадобилось
    //   head          - голова ответила сама, дальше ничего не пошло
    //   head/hyper    - голова назвала адрес, забрал его прыжок (голова окупилась)
    //   head/combine  - голова адреса не знала, собираем сами (запрос потрачен впустую)
    //   direct        - RequestDirect, мимо словаря
    //
    // Наружу поле отдаём всегда, а печатают его только dev-выводы: по дороге читается «до и после
    // персиста», и миру эта кухня не нужна.
    public string LastRoad = "";

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
    private const int PhaseHead = 9;
    private const int PhaseCredit = 10;

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

    // Ёмкость словаря прыжков. Дальше он не растёт: новый адрес вытесняет самый старый - кольцо,
    // перезапись с начала. Сервер греет сотни адресов и копит их дальше, а держать их все на
    // клиенте незачем - вытесненный просто соберётся обычной дорогой и вернётся в словарь.
    //
    // Ёмкость ОБЯЗАНА быть не меньше сида, иначе он съедает сам себя: сид приезжает одной пачкой,
    // и хвост пачки вытесняет её же начало - на 263 при сиде 265 первым выпадал посев carts/5,
    // и шаг «hyper from db» шёл сборкой. Держим с запасом над серверным SeedLimit (512).
    private const int MaxRemembered = 1024;

    // Порядок вселения: индекс кольца -> адрес, который его занимает. Нужен, чтобы знать, кого
    // выселять: у DataDictionary своего порядка нет.
    private string[] jumpRing = new string[MaxRemembered];
    private int jumpRingAt;

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
        ForgetJumps();

        Begin(true);
    }

    // Два адреса за раз - частный случай пачки, так их называет разработчик, потребитель тулзы.
    //
    // Соседние номера уйдут одним запросом диапазоном, разные - по очереди. Решает это Run,
    // отдельной логики тут больше нет.
    public void RequestPair(string first, string second)
    {
        Queue(first);
        Queue(second);

        Run();
    }

    // Забыть СВОИ прыжки, не трогая серверные. Нужно, чтобы увидеть персист как его видит новый
    // игрок: клиент про адрес не знает ничего, а сервер отдаёт его прыжок сидом в connect.
    public void ForgetJumps()
    {
        jumps = new DataDictionary();
        jumpRing = new string[MaxRemembered];
        jumpRingAt = 0;
    }

    // Забыть ОДИН номер, оставив остальные. Так выглядит адрес, которого этот клиент не застал:
    // сервер его помнит, а у нас его нет - ровно случай, ради которого и заведена голова. В мире
    // это происходит само, когда адрес собрал другой игрок; в демке нужен способ вызвать это
    // намеренно, иначе сид в connect выдаёт клиенту всё и голове нечего искать.
    public void ForgetJump(string url)
    {
        jumps.Remove(PayloadOf(url));
    }

    // Кладёт прыжок, вытесняя самый старый, если кольцо заполнено.
    private void KeepJump(string url, int jump)
    {
        if (jumps.ContainsKey(url)) { jumps.SetValue(url, jump); return; }

        string evicted = jumpRing[jumpRingAt];

        if (evicted != null && evicted != "") jumps.Remove(evicted);

        jumpRing[jumpRingAt] = url;
        jumpRingAt = (jumpRingAt + 1) % MaxRemembered;

        jumps.SetValue(url, jump);
    }

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

    public void Request(string url) => Dispatch(url, true);

    // ПАЧКА: адреса копятся, пока их не попросят все разом.
    //
    // Зачем: пока запросы идут по одному, клиент не знает, что будет дальше, и каждый адрес
    // проходит свой путь. Собранная пачка позволяет разложить её ОДИН раз - кого сервер знает по
    // номеру (тем прыжок), кого нет (тем голова), - и не гонять поиск там, где номер уже на руках.
    // Порядок - живая очередь: кто встал раньше, тот раньше и уедет. Это важно там, где адрес
    // приходится СОБИРАТЬ: поток сборки на сервере один, две сборки разом в него не влезут, и
    // вторая ждёт, пока первая закроется хвостом.
    //
    // Мест 2048. Больше - отказ, а не молчаливая потеря: место освободится, когда пачка добежит,
    // и адрес можно поставить снова.
    public bool Queue(string url) => Enqueue(url, false);

    // ГЛАВНЫЙ вход для мира: попросить адрес и получить КЛЮЧ, по которому появится результат.
    //
    // Возврата данных здесь быть не может - Udon не ждёт, а строку по ссылке не передашь: она
    // неизменяема и едет значением. Поэтому отдаём ключ, а тело кладём в словарь тулзы, и юзер
    // спрашивает его когда захочет:
    //
    //     string key = client.Require("https://site.com/a/1");
    //     ...
    //     string body = client.Result(key);   // "" пока не приехало, непусто - пришло
    //
    // Очередь копится САМА: каждый Require добавляет адрес, и пачка уходит либо когда набралось
    // четыре (потолок диапазона, больше в один запрос всё равно не влезет), либо когда кончился
    // кадр. Кадр тут - аналог области видимости: всё, что мир попросил за один заход, и есть одна
    // пачка. Занят клиент - копится дальше и уедет, как освободится.
    public DataList Require(string url) => Ask(url, false);

    // То же, но адрес поедет мимо словаря, напрямую.
    public DataList RequireDirect(string url) => Ask(url, true);

    private DataList Ask(string url, bool direct)
    {
        string key = PayloadOf(url);

        // КОРОБКА - она же результат. Строку вернуть нельзя: она неизменяема и едет значением,
        // дописать в неё задним числом невозможно. А список - ссылка: тулза положит тело в нулевой
        // слот, и переменная у мира перестанет быть пустой сама.
        DataList box = boxes.TryGetValue(key, out DataToken had) && had.TokenType == TokenType.DataList
            ? had.DataList
            : new DataList();

        if (box.Count == 0) box.Add("");

        boxes.SetValue(key, box);

        if (!Enqueue(url, direct)) return box;

        if (!bodies.ContainsKey(key)) bodies.SetValue(key, "");

        // Флаг ставим ВСЕГДА, а отправку лишь пробуем. Клиент занят - пачка спокойно копится, и
        // насос дошлёт её сам, как только поток освободится. Мир про паузы SDK не знает и знать не
        // должен: его дело попросить, ждать - наше.
        flush = true;

        // Второй, НЕЗАВИСИМЫЙ насос. Один Update - это одна точка отказа: пока он по какой-то
        // причине не отрабатывал, очередь стояла и уезжала только с чужим Run - то есть когда в
        // клиент постучится другая кнопка. Снаружи это выглядит как «мой запрос ждёт соседнего
        // рига», чего быть не должно вовсе. Отложенное событие идёт мимо кадрового цикла поведения
        // и ведёт себя иначе, так что вдвоём они друг друга страхуют.
        if (!pendingFlush) { pendingFlush = true; SendCustomEventDelayedFrames(nameof(Flush), 1); }

        if (!busy && queued.Length >= RangeMax) Run();

        return box;
    }

    // Коробки результатов: адрес -> список из одного слота, выданный наружу.
    private DataDictionary boxes = new DataDictionary();

    // Кладёт тело в коробку адреса, если её просили.
    private void Fill(string payload, string body)
    {
        if (!boxes.TryGetValue(payload, out DataToken had) || had.TokenType != TokenType.DataList) return;

        had.DataList.SetValue(0, body);
    }

    // Содержимое коробки одной строкой - для тех, кому удобнее так.
    public string Result(DataList box)
    {
        if (box == null || box.Count == 0) return "";

        return box.TryGetValue(0, out DataToken body) && body.TokenType == TokenType.String ? body.String : "";
    }

    // Конец кадра - конец «области видимости» пачки. Тут она и уходит, если не набралась раньше.
    //
    // Пока клиент занят, здесь ничего не происходит и флаг остаётся поднятым: следующий кадр
    // попробует снова, и так до тех пор, пока поток не освободится. Это и есть автоматическая
    // подача - мир зовёт Require когда хочет, а очередь уезжает так быстро, как позволяет SDK.
    private bool flush;

    // Насос уже в пути - второй заводить незачем, иначе на каждый Require копился бы свой.
    private bool pendingFlush;

    // Update, а не LateUpdate: Interact отрабатывает раньше кадрового Update, поэтому всё, что мир
    // попросил за один заход, всё равно уедет одной пачкой - зато событие простое и заведомо живое.
    private void Update()
    {
        if (!flush || busy || queued.Length == 0) return;

        Run();
    }

    // Тот же насос, но по отложенному событию. Занят клиент - не бросаем очередь, а приходим ещё
    // раз: без этого одна неудачная попытка оставляла бы пачку лежать до следующего Require.
    public void Flush()
    {
        if (!flush || queued.Length == 0) { pendingFlush = false; return; }

        if (busy) { SendCustomEventDelayedSeconds(nameof(Flush), 0.25f); return; }

        pendingFlush = false;

        Run();
    }

    // То же место в очереди, но адрес поедет НАПРЯМУЮ, мимо словаря. Нужно, когда заранее известно,
    // что фрагменты его не возьмут: пачка тогда не обязана быть однородной.
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

    private bool[] queuedDirect = new bool[0];
    private bool[] batchDirect = new bool[0];

    // Сколько адресов помещается в очередь до Run.
    private const int MaxQueued = 2048;

    // Выполняет накопленное. Тела складываются по адресам, забрать их - BodyOf(url).
    public void Run()
    {
        if (busy || queued.Length == 0) return;

        // Словарь тел НЕ пересоздаём: ключи из Require выданы наружу заранее, и новая пачка не
        // имеет права обнулить результат предыдущей - юзер мог ещё не прочитать.
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

    // Выбирает следующий шаг пачки: сперва ищет ДИАПАЗОН - адреса, чьи номера идут подряд, - и
    // забирает их одним запросом. Не нашлось серии - берёт первый несделанный поодиночке.
    private void NextInBatch()
    {
        // Берём САМЫЙ МЛАДШИЙ номер из несделанных и просим от него диапазон. Сервер отдаёт семью
        // - узел и соседей по родителю, - поэтому подряд идущие номера в самой пачке не нужны:
        // остальные её адреса, скорее всего, приедут этим же запросом и отметятся сделанными.
        int start = -1;

        for (int i = 0; i < batch.Length; i++)
        {
            if (done[i] || batchDirect[i]) continue;

            int first = JumpOf(PayloadOf(batch[i]));

            if (first < 0) continue;

            if (start < 0 || first < start) start = first;
        }

        if (start >= 0) { SendRange(start, RangeMax); return; }

        // Прямой адрес шлём как просили - мимо словаря; остальные обычной дорогой.
        for (int i = 0; i < batch.Length; i++)
            if (!done[i]) { Dispatch(batch[i], !batchDirect[i]); return; }

        batch = new string[0];

        Finish();
    }

    // Забирает диапазон одним запросом: /h/<первый>/<сколько>/<подпись>.
    private void SendRange(int first, int length)
    {
        LastError = "";
        forwarded = "";
        forwardedBody = "";
        pendingUrl = "";
        LastQueries = 0;
        LastJump = first;
        busy = true;

        // Диапазон - это прыжок, и дорога у него прыжковая. Без этой строки в выводе оставалась
        // прошлая: пачка из четырёх уезжала одним запросом, а числилась сборкой.
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

    // Разбирает долг: складывает пришедшие тела по адресам и отмечает их выполненными.
    private void TakeDebt(string json)
    {
        if (!VRCJson.TryDeserializeFromJson(json, out DataToken root)) return;
        if (root.TokenType != TokenType.DataDictionary) return;

        DataDictionary answer = root.DataDictionary;

        LastPending = DictInt(answer, "pending");

        if (LastPending < 0) LastPending = 0;

        if (!answer.TryGetValue("ready", out DataToken list) || list.TokenType != TokenType.DataList) return;

        DataList ready = list.DataList;

        // Набор -> что этим ответом ему погасили. Строка ответа принадлежит ОДНОМУ набору: тела в
        // одном довеске часто из разных, и валить их в кучу значит потерять, чей это ответ.
        DataDictionary touched = new DataDictionary();

        // Он же, но с телами - для отдельной панели, где видно не размер, а само содержимое.
        DataDictionary bodyLines = new DataDictionary();

        for (int i = 0; i < ready.Count; i++)
        {
            if (!ready.TryGetValue(i, out DataToken item) || item.TokenType != TokenType.DataDictionary) continue;

            string url = PayloadOf(DictString(item.DataDictionary, "url"));

            if (url == "") continue;

            string body = DictString(item.DataDictionary, "response");

            bodies.SetValue(url, body);

            // И в коробку, если этот адрес просили через Require: она у мира на руках, и именно
            // по ней он узнаёт, что тело приехало.
            Fill(url, body);

            Mark(url);

            // Своё тело кладём и туда, откуда его берёт одиночный запрос.
            if (url == pendingUrl) forwardedBody = body;

            // Чей это ответ: номер запроса, который за этим адресом и пошёл. Почти всегда он НЕ
            // тот, что привёз тело, - в этом весь смысл долга, и без номера пару не составить.
            int from = -1;

            if (asking.TryGetValue(url, out DataToken owner) && owner.TokenType == TokenType.Int) from = owner.Int;

            // Тело приехало - это и есть получение, единственное место, где ставится vresponse.
            // Номер в скобках ТОТ ЖЕ, что у vrequest: пара сходится по нему, а не по порядку строк.
            Named(url, from < 0 ? "vresponse" : "vresponse[" + from + "]");

            string named = TailOf(url) + " " + body.Length + "b/" + DictInt(item.DataDictionary, "elapsedMs") + "ms";

            if (from >= 0)
            {
                string was = touched.TryGetValue(from, out DataToken had) && had.TokenType == TokenType.String ? had.String : "";

                touched.SetValue(from, was == "" ? named : was + ", " + named);

                // Тело копим отдельно: строку с ним допишет Report, когда узнает, закрылся набор
                // этим ответом или нет - от этого зависит, response это или vresponse.
                string kept = bodyLines.TryGetValue(from, out DataToken was2) && was2.TokenType == TokenType.String ? was2.String : "";
                string shown = Cut(body);

                bodyLines.SetValue(from, kept == "" ? shown : kept + " | " + shown);
            }

            SettleUrl(url);
        }

        // Отчитываемся ПО НАБОРАМ, которых этот ответ коснулся: закрылся - vresponse с полным
        // временем, не закрылся - response с тем, сколько прошло от его отправки.
        DataList ids = touched.GetKeys();

        for (int k = 0; k < ids.Count; k++)
        {
            if (!ids.TryGetValue(k, out DataToken id) || !touched.TryGetValue(id, out DataToken paid)) continue;

            string shown = bodyLines.TryGetValue(id, out DataToken kept) && kept.TokenType == TokenType.String ? kept.String : "";

            Report(id, paid.String, shown);
        }
    }

    // Строка о наборе: что ему погасили этим ответом и сколько он уже длится. Тела уходят своей
    // строкой на отдельную панель - там видно не размер, а само содержимое.
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

        // Копим по набору: строка обязана показывать все его ответы, а не только этот довесок.
        if (record.TryGetValue(4, out DataToken hadPaid) && hadPaid.TokenType == TokenType.String)
            record.SetValue(4, hadPaid.String == "" ? paid : hadPaid.String + ", " + paid);

        if (record.TryGetValue(5, out DataToken hadData) && hadData.TokenType == TokenType.String && shown != "")
            record.SetValue(5, hadData.String == "" ? shown : hadData.String + " | " + shown);

        // Накопленное держим отдельно от пришедшего СЕЙЧАС: response говорит про этот ответ,
        // vresponse - про весь набор.
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

        // Погашено всё - набор закрыт, и это его последняя строка: полное время от отправки.
        //
        // Подробности приехавшего несём ЗДЕСЬ же. Иначе они пропадают: набор из четырёх обычно
        // гасится одним долгом, промежуточной строки не возникает вовсе, и размеры тел теряются.
        string who = id.Int + kind.String;

        // СНАЧАЛА response - что принёс именно этот ответ. Он есть всегда, даже когда набор им же
        // и закрывается: иначе не видно, чем набор набирался, а видно только итог.
        // Короткий взгляд на пришедшее - в той же строке: убедиться, что приехал json нужного
        // адреса, а не пустышка. Целиком тела уходят на свою панель.
        string peek = shown == "" ? "" : "   " + (shown.Length <= Peek ? shown : shown.Substring(0, Peek) + "...");

        Trace("response: " + who + " погашено [" + paid + "], ждём [" + (owed == "" ? "" : owed) + "]   " + spent + peek, false);

        Data(who + " response [" + Joined(urls) + "]", shown);

        if (owed != "") return;

        // Набор закрыт. Строка ИТОГОВАЯ, а не повтор предыдущей: адреса, сколько ответов на них
        // ушло и полное время. Подробности по каждому телу уже сказаны строками response.
        Trace("vresponse: " + who + " " + Joined(urls) + "   " + urls.Count + " urls за "
            + answers + (answers == 1 ? " ответ" : " ответа") + "   " + spent, false);

        Data(who + " vresponse [" + Joined(urls) + "]", allShown);

        openRequests.Remove(id);
    }

    // Сколько запросов ушло с подключения. LastQueries считает один адрес и сбрасывается, а долгу
    // нужен сквозной номер: только по нему видно, какой именно запрос его притащил.
    public int TotalQueries;

    // Сколько адресов сервер ещё не донёс. Больше нуля - значит будет следующий долг.
    public int LastPending;

    // Адрес -> номер запроса, который за ним пошёл. Нужен только логу: тело приезжает долгом, и
    // сказать «чей это ответ» иначе нечем.
    private DataDictionary asking = new DataDictionary();

    // Эндпоинт последнего отправленного запроса.
    private string route = "";

    // Незакрытые vrequest'ы: номер -> [эндпоинт, адреса, погашено].
    //
    // vrequest - сущность КЛИЕНТСКАЯ и неявная: это батчинг фронта, а не свойство провода. Сервер
    // о нём не знает и знать не должен, номер у него чисто клиентский, а состав - массив адресов,
    // ушедший одной строкой; массив из одного адреса это тоже массив.
    //
    // Держим его только ради последней строки: чтобы сказать «набор закрыт целиком», надо помнить,
    // что в нём было. Пришло тело - вычеркнули; вычеркнули последнее - забыли.
    //
    //   vrequest  - ушёл, вот его адреса;
    //   response  - пришёл физический ответ, вот что он погасил и что осталось;
    //   vresponse - погашен последний адрес, набор закрыт.
    private DataDictionary openRequests = new DataDictionary();

    // Своя нумерация: vrequest'ы считаются отдельно от физических запросов. Первый набор - первый,
    // а не второй лишь потому, что connect ушёл раньше.
    private int vrequests;

    // ЖУРНАЛ: ровно те строки, что уходят в консоль. Доска печатает их дословно, поэтому консоль и
    // канвас рассказывают об одном прогоне одно и то же, а не два разных рассказа.
    //
    // Разделён надвое: что ушло и что вернулось. На риге это две панели рядом, и по ним видно, как
    // ответы отстают от запросов - в одной колонке это тонет.
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

    // Сами тела: чей набор, какой адрес, и что пришло. В консоль их не шлём - там они забьют всё,
    // тела бывают по сорок килобайт; на панели держим начало, этого хватает, чтобы убедиться.
    private string payloads = "";

    public string TakeData()
    {
        string all = payloads;

        payloads = "";

        return all;
    }

    // Строка тел на панель: чей набор, чем он был - response или vresponse, - весь его список
    // адресов и сами данные. Одна строка на набор, а не на адрес: набор и есть единица.
    private void Data(string who, string shown)
    {
        if (shown == "") return;

        payloads = payloads == "" ? who + " " + shown : payloads + "\n" + who + " " + shown;
    }

    // Начало тела: длинное режем, переводы строк убираем - иначе одна ответка растянет панель.
    private string Cut(string body)
    {
        string flat = body.Replace("\n", " ").Replace("\r", " ");

        return flat.Length <= DataCut ? flat : flat.Substring(0, DataCut) + "...";
    }

    private const int DataCut = 160;

    // Сколько символов json показать прямо под строкой response - «кратенько», для сверки.
    private const int Peek = 200;

    private void Trace(string line, bool request)
    {
        if (request) outgoing = outgoing == "" ? line : outgoing + "\n" + line;
        else incoming = incoming == "" ? line : incoming + "\n" + line;

        // Пишем в консоль в ОБОИХ модах. Prod пока отличается от dev ровно одним - полным
        // персистом хайперов; выводы там те же, иначе проверять релиз нечем.
        Debug.Log("[CombineQueries] " + line);
    }

    // Заводит набор и возвращает его номер. Пачка - это и один адрес тоже: хвост несёт ровно один,
    // и он такой же vrequest, как диапазон из четырёх.
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

        // Время набора считаем от ОТПРАВКИ запроса, а не от разбора его ответа: состав мы узнаём
        // из ответа, но ушёл-то он раньше. Иначе у каждого набора выходил бы ровно один кулдаун
        // SDK - пять секунд, - а не то, сколько он на самом деле шёл до последнего тела.
        record.Add(lastLoadAt);

        // Копилки: что набору уже принесли, какими телами и сколькими ответами.
        record.Add("");
        record.Add("");
        record.Add(0);

        openRequests.SetValue(id, record);

        // Чей адрес - помним здесь же: тело приедет долгом, и подписать его иначе нечем.
        for (int i = 0; i < urls.Count; i++)
            if (urls.TryGetValue(i, out DataToken url) && url.TokenType == TokenType.String)
                asking.SetValue(url.String, id);

        Trace("vrequest: " + id + route + " " + Joined(urls), true);

        return id;
    }

    // Гасит адрес во всех открытых наборах. Печатает не здесь: строку выдаёт Report, он же знает,
    // что этим ответом погашено и сколько времени набор уже идёт.
    // Имя не Settle: так зовётся публичный добор долга, и SendCustomEvent по имени их бы спутал.
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


    // Список адресов через запятую.
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

    // Разбирает, ЗА ЧЕМ ушёл прыжок. Диапазон тащит четыре РАЗНЫХ адреса за один запрос, и номера
    // соседей узнать больше неоткуда - забираем их в кольцо здесь, даром.
    //
    // Тел в этом ответе нет: сервер не ждёт чужой сервер, он только называет. Результаты печатает
    // долг - в том выводе, куда они реально доехали.
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

            // Адрес назван - значит за ним УЖЕ пошли, и просить его вторым запросом незачем. Тело
            // приедет долгом. Без этой отметки пачка досылала прыжок за каждым, кого диапазон
            // подобрал по дороге, и три гипера превращались в два-три запроса вместо одного.
            Mark(PayloadOf(url));

            Named(url, "vrequest[" + TotalQueries + "] hyper");

            asked.Add(PayloadOf(url));
        }

        OpenRequest(asked);
    }

    // Дописывает адрес в строку «отправлено» вместе с его дорогой. Дорога у каждого своя: в одной
    // пачке два адреса уезжают прыжком, один голова называет, а два оставшихся собираются - и без
    // пометки у каждого не видно, кто чем уехал.
    //
    // Пометка говорит, ЧТО ИМЕННО произошло с адресом:
    //   vrequest - ушёл наружу. Неважно, просили его или сервер прихватил заодно: запрос за ним
    //              всё равно уехал, и стоит он столько же.
    //   vresponse - приехало ТЕЛО. Только это и значит «получено», и приезжает оно долгом, часто
    //              уже в другом шаге - поэтому пометка и нужна отдельная.
    //   head     - ни то, ни другое: голова назвала адрес и дала номер, наружу за ним не ходили.
    private void Named(string url, string mark)
    {
        string named = TailOf(url) + " " + mark;

        LastSent = LastSent == "" ? named : LastSent + ", " + named;
    }

    // Конец адреса - всё после хоста. Именно им адреса и различаются, а хост у них общий.
    private string TailOf(string url)
    {
        string payload = PayloadOf(url);
        int cut = payload.IndexOf("/");

        return cut < 0 || cut + 1 >= payload.Length ? payload : payload.Substring(cut + 1);
    }

    // Просит сервер отдать долг, ничего нового не запрашивая: /h/<любой>/0/<подпись>.
    // Нужно тому, кому тело нужно немедленно, - иначе оно приедет с ближайшим обычным запросом.
    public void Settle()
    {
        if (busy) return;

        LastError = "";
        forwarded = "";

        // Тело прошлого адреса НЕ трогаем: за своим мы не идём, значит и затирать нечем. Чистит
        // его следующий настоящий запрос - там оно и правда устаревает.
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

    // Тело ответа по адресу из пачки. Пусто - адрес не запрашивали либо он не дошёл.
    public string BodyOf(string url)
    {
        if (!bodies.TryGetValue(PayloadOf(url), out DataToken body)) return "";

        return body.TokenType == TokenType.String ? body.String : "";
    }

    // Сколько запросов ушло на всю пачку. LastQueries считает только последний адрес.
    public int BatchQueries;

    private string[] queued = new string[0];
    private string[] batch = new string[0];
    private DataDictionary bodies = new DataDictionary();


    public void RequestDirect(string url) => Dispatch(url, false);

    // Занят ли клиент. Один на сцену, а кнопок, которые в него ходят, несколько - по этому флагу
    // риг и решает, можно ли просить.
    //
    // TRUE с момента, как ушёл ПЕРВЫЙ запрос дела, и до события «готово». В деле может быть много
    // физических запросов - подключение, куски сборки, хвост, прыжки пачки, - и всё это время
    // клиент занят одним потоком: у сервера СОБИРАЕТСЯ один адрес, и чужой кусок его испортит.
    // Поднимают его connect, Remember, Request, RequestDirect, Run и Settle.
    //
    // FALSE, когда дело закрыто: пришёл последний ответ и Done снял флаг - неважно, успехом или
    // ошибкой. Долг на это не влияет: тела могут ещё ехать, но поток свободен, и просить можно.
    //
    // Занятому клиенту просить бесполезно и НЕ опасно: Require молча выйдет, ничего не сломав.
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

        // Тело прошлого адреса чистим ЗДЕСЬ. Иначе, пока долг за новым ещё летит, наружу отдаётся
        // предыдущее - и выглядит это ровно как кеш ответов, которого у клиента нет.
        forwardedBody = "";

        // В пачке строку «отправлено» не чистим: она копится по всем её адресам, чтобы в конце
        // было видно, кто уехал прыжком, кого назвала голова, а кого пришлось собирать.
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

    // Тело последнего адреса. Канал доставки один - долг: сервер не ждёт чужой сервер, поэтому
    // тело приезжает либо в том же ответе (успел), либо в следующем. Здесь лежит уже разобранное.
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

        // ЗАКРЫВАЮЩИЙ КУСОК. Адрес кончается ровно на границе фрагмента - хвосту нести нечего, и
        // диктовать кусок отдельным запросом незачем: /cf делает и то, и другое разом.
        //
        // Берём только младшие адреса словаря: пул закрытия это CloseLimit * подписи, и на весь
        // словарь он стоил бы полсотни мегабайт вместо неполного.
        if (tail == 0 && count > 0 && k[count - 1] == 1 && q[count - 1] < CloseLimit) k[count - 1] = 8;
        else { q[count] = tail; k[count] = 2; count++; }

        // Номер спрашиваем ОДИН раз: он нужен и голове (звать ли её), и прыжку, а два вызова
        // подряд писали в лог «no jump» дважды на каждый адрес.
        int jump = JumpOf(payload);

        // Прыжка нет, но сервер мог собрать этот адрес для кого-то другого - спросим голову.
        // Она стоит один запрос и называет адрес, если он ей знаком.
        if (jump < 0 && !headTried && SendHead(payload)) return;

        LastJump = -1;

        int skip = 0;

        // Прыжок закрывает адрес ЦЕЛИКОМ - сервер знает и хвост, поэтому форвардит сам.
        // Правило простое: хайпер это один запрос. Не попали в словарь - идём обычной дорогой,
        // combine + tail по динамической фрагментации.
        if (jump >= 0 && jump < MaxJumps) skip = count;

        // Голова уже отработала - значит дорога у нас составная, и стоит она на запрос дороже.
        //
        // Имена одни и те же в обоих модах: prod пока отличается от dev только полным персистом,
        // и урезать ему вывод рано - иначе релиз нечем проверять.
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

        // Эндпоинт запроса - им подписан и сам запрос, и его ответ: «1/h», «4/t».
        route = kind == 0 || kind == 1 || kind == 3 ? "/c"
              : kind == 4 || kind == 5 ? "/h"
              : kind == 6 ? "/hd"
              : kind == 7 ? "/tc"
              : kind == 8 ? "/cf"
              : fragments ? "/t" : "/d";

        if (kind == 0) { Load(PhaseChunks, ChunkPool[queue[queuePos]]); return; }

        if (kind == 1) { Load(PhaseFragment, VfPool[queue[queuePos]]); return; }

        // Развязка-3: старший разряд бесконечного адреса, сдвиг делает сервер.
        if (kind == 3) { Load(PhaseFragment, HopPool[queue[queuePos]]); return; }

        // Одиночный прыжок берёт ДИАПАЗОН, а не один адрес: запрос стоит столько же, а соседи по
        // номеру приезжают даром - телами в долг и номерами в кольцо. Отдельного пула ему не
        // нужно, это экономит 32 768 печёных ссылок (~4 МБ).
        if (kind == 4) { Load(PhaseJump, RangePool[(queue[queuePos] * RangeMax + RangeMax - 1) * JumpSignValues + NextSign()]); return; }

        // Пачка: индекс уже посчитан вместе с подписью, здесь только шлём.
        if (kind == 5) { Load(PhaseJump, RangePool[queue[queuePos]]); return; }

        // Голова: индекс тоже посчитан заранее.
        if (kind == 6) { Load(PhaseHead, HeadPool[queue[queuePos]]); return; }

        // Только долг: ничего не запрашиваем, забираем доспевшее.
        if (kind == 7) { Load(PhaseCredit, CreditPool[queue[queuePos]]); return; }

        // Кусок И закрытие одним запросом. Ответ тот же, что у хвоста, поэтому и фаза его.
        if (kind == 8) { Load(PhaseTail, ClosePool[queue[queuePos] * SignValues + NextSign()]); return; }

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
        // Прыжок и голова разбираются ПЕРВЫМИ: сколько адресов ушло, знает только их ответ - до
        // него у клиента на руках один адрес, а уехало четыре. Поэтому список «что отправлено»
        // печатается здесь, а не в момент отправки: раньше его взять неоткуда.
        if (phase == PhaseJump) TakeSent(response.Result);
        else if (phase == PhaseHead) headTaken = TakeHead(response.Result);

        // Сервер не ждёт чужие сервера: он отвечает сразу, а тела приезжают ДОЛГОМ - с этим же
        // ответом, если успели, иначе со следующим запросом, каким бы он ни был. Поэтому долг
        // разбираем до всего остального: там может лежать и то, чего мы ждём прямо сейчас.

        TakeDebt(response.Result);

        // Погашение долга: в ответе только он, разбирать больше нечего.
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

            // Про сам connect в лог не пишем: в консоли остаются только request/vrequest и их
            // ответы, всё прочее - шум, за которым не видно потока.
            Done();
            return;
        }

        if (phase == PhaseChunks || phase == PhaseFragment) { queuePos++; SendNext(); return; }

        // Голова: сервер вернул всё, что знает про этот кусок. Свой адрес забираем из словаря,
        // чужие кладём в кольцо - они пригодятся дальше и достались бесплатно.
        if (phase == PhaseHead)
        {
            // Разобрали выше, до строки ответа: список найденного печатается раньше, чем долг.
            int taken = headTaken;

            // Второй запрос нужен НЕ всегда: тело нашего адреса могло приехать долгом прямо с
            // ответом головы - за ним ходили раньше, и оно доспело. Тогда брать уже нечего.
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

            // Голова только назвала адреса - забирать их идёт прыжок, номер теперь в кольце.
            // Это дорога head/hyper: два запроса на адрес, которого клиент не собирал ни разу.
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

            // Своего адреса среди найденных нет - собираем сами. Но кусок, который уехал с
            // головой, сервер придержал: он приклеится концом при закрытии, и диктовать надо
            // ТОЛЬКО НАЧАЛО. Запрос за голову тем самым не пропал - он оплатил последний кусок.
            string kept = StringField(response.Result, "kept");

            SendCombine(kept == "" ? pendingUrl : pendingUrl.Substring(0, pendingUrl.Length - kept.Length));
            return;
        }

        // Прыжок отдаёт адрес и тело сразу - это и есть весь запрос.
        if (phase == PhaseJump)
        {
            // Сервер такого узла не знает (например, базу почистили): забываем прыжок и идём
            // обычной дорогой - собираем адрес с нуля.
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

            // Хвост уносит ровно один адрес - свой. Называем и его: строка «отправлено» обязана
            // быть заполнена на любой дороге, иначе по ней не сравнить сборку с прыжком.
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
        TotalQueries++;
        lastLoadAt = Time.time;

        // Что ушло: vrequest - если за запросом стоит МАССИВ адресов, собранный фронтом, и просто
        // request - если такого массива нет вовсе. Массив из одного это тоже массив.
        //
        // Хвост знает свой адрес уже сейчас - открываем набор здесь, и номера идут в порядке
        // отправки. Прыжок и голова молчат: их состав называет сервер, набор откроется из ответа.
        // Куски сборки, connect, код и добор долга ничего не батчат - это request.
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

        KeepJump(pendingUrl, leaf);
    }

    // Спрашивает сервер об адресе ОДНИМ запросом: кусок расхождения плюс обрезок номера похожего.
    //
    // Зовётся только когда база НАЙДЕНА: без неё сервер не достроит адрес, ответит списком похожих,
    // и запрос уйдёт впустую - дешевле сразу диктовать combine'ами. Возвращает false, если спросить
    // нечем.
    private bool SendHead(string payload)
    {
        int cut = payload.LastIndexOf("/");

        if (cut < 0 || cut + 1 >= payload.Length) return false;

        // Расхождение - последний сегмент адреса. Именно им похожие адреса и отличаются.
        string differs = payload.Substring(cut + 1);
        string common = payload.Substring(0, cut + 1);

        int piece = -1;

        for (int i = 0; i < cachedFragments.Length; i++)
            if (cachedFragments[i] == differs && cachedFragIds[i] < HeadLimit) { piece = cachedFragIds[i]; break; }

        if (piece < 0) return false;

        // База - любой известный адрес с тем же началом: сервер подставит наш кусок вместо его
        // последнего сегмента и получит искомое.
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

    // Голову спрашиваем не больше раза на адрес: не нашла - собираем, второй заход только сожрёт
    // запрос.
    private bool headTried;

    // Сколько адресов назвала голова в последнем ответе. Разбор идёт раньше остального, поэтому
    // результат приходится донести до ветки полем.
    private int headTaken;

    // Разбирает ответ головы: кладёт названные адреса в кольцо. Тел здесь нет - голова наружу не
    // ходит, забирает их прыжок, а приезжают они долгом.
    //
    // Возвращает, сколько адресов нашлось, либо -1 - нашего среди них нет и надо собирать самим.
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

        // Голова тоже несёт от одного до нескольких адресов - открываем её набор и тут же закрываем:
        // её продукт это ИМЕНА, а не тела, ждать по ней нечего. Тела возьмёт прыжок, своим номером.
        int head = OpenRequest(asked);

        for (int i = 0; i < asked.Count; i++)
            if (asked.TryGetValue(i, out DataToken url) && url.TokenType == TokenType.String) SettleUrl(url.String);

        if (head > 0) Report(head, "имена", "");

        return ours;
    }

    // Номер адреса или -1. В лог здесь не пишем: пачка спрашивает номер у каждого своего адреса на
    // каждом проходе, и «нет номера» печаталось бы по четыре раза подряд, разрывая пары
    // vrequest/vresponse. Что номера не было, и так видно по дороге - там стоит combine.
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

        // Пачка: раскладываем пришедшее и берёмся за следующий кусок очереди. Наружу сообщаем один
        // раз, когда она кончится - иначе потребитель получит событие на каждый адрес.
        if (batch.Length > 0)
        {
            TakeBatch();

            if (LastError == "") { NextInBatch(); return; }

            batch = new string[0];
        }

        Finish();

        // Слать больше нечего, а за сервером ещё числятся тела - идём за ними сами, и так пока
        // долг не погасится. Настоящий запрос отменяет это сам собой: Settle уступает занятому.
        if (LastPending > 0) SendCustomEventDelayedSeconds(nameof(Settle), CreditDelay);
    }

    // Пауза перед добором долга. Меньше - и мы дёргаем сервер вхолостую, пока форвард ещё летит.
    private const float CreditDelay = 0.5f;

    // Отмечает адрес пачки отработанным. Тело могло уже приехать долгом, а могло ещё лететь -
    // в обоих случаях запрашивать его снова не нужно.
    private void TakeBatch()
    {
        if (pendingUrl == "") return;

        if (!bodies.ContainsKey(pendingUrl)) bodies.SetValue(pendingUrl, Take());

        Fill(pendingUrl, Take());

        Mark(pendingUrl);
    }

    // Отмечает адрес пачки выполненным - больше его не запрашиваем.
    private void Mark(string payload)
    {
        for (int i = 0; i < batch.Length; i++)
            if (!done[i] && PayloadOf(batch[i]) == payload) { done[i] = true; return; }
    }

    private void Finish()
    {
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

            KeepJump(url, jump);
            SeedJumps++;
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

    // Индекс = (первый * max + сколько-1) * signs + подпись. Во втором сегменте едет ИМЕННО
    // «сколько» (1..max), а не индекс: сервер читает его как длину диапазона.
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

    // Индекс = (кусок * bases + база) * signs + подпись. Прямоугольник, а не квадрат: кусков много
    // (весь L2), а баз мало - от гипера едет только обрезок номера, в этом и экономия.
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
