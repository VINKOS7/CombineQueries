using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Data;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

// РЕЛИЗНЫЙ РИГ: одна кнопка, и в ней весь смысл тулзы.
//
// Мир считает, сколько шагов прошёл игрок, и по нажатию идёт по адресам, в которых это число стоит
// прямо в пути. Таких адресов не существовало до нажатия, запечь их заранее нельзя - а Udon умеет
// грузить только запечённое. Поэтому кнопка и показывает то, чего в мире без тулзы не бывает.
//
// Одно нажатие - ДВЕ пачки ПО ОЧЕРЕДИ: сперва четыре адреса; как только они все пришли, следом два.
// Номер идёт от числа шагов и растёт на единицу сквозь обе: при 7 шагах первая пачка это
// products/7..10, вторая - products/11 и 12. Каждая пачка это свои Require и ОДИН Result, который
// её и выпускает, и у каждой своё время - от её собственного vrequest.
//
// Нажатие ГЛОБАЛЬНОЕ: сетевым событием оно уходит всем игрокам, и каждый выполняет его у себя -
// своим клиентом и в свою доску. Синхронизируется только снимок доски - для тех, кто зашёл позже.
//
// Риг НЕЗАВИСИМ от остальных: он не перехватывает событие клиента и не читает его журнал. Просит
// через Require, получает коробки и ждёт по ним результат - так же, как это будет делать любой мир,
// взявший тулзу.
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class SampleSteps : UdonSharpBehaviour
{
    public CombineQueries client;

    [Tooltip("Начало адреса. Число шагов дописывается в конец")]
    public string urlPrefix = "https://dummyjson.com/products/";

    [Tooltip("Куда писать свой поток")]
    public Text output;

    [Tooltip("Метров на шаг")]
    public float stepLength = 0.75f;

    private float walked;
    private int steps;
    private Vector3 was;
    private bool started;

    // Куб самой кнопки. Пока её запрос в пути, она гаснет: заблокированная кнопка, которая выглядит
    // как обычная, неотличима от сломанной - жмёшь и не понимаешь, почему ничего не происходит.
    private Renderer cube;
    private Color idle;

    private void Start()
    {
        cube = GetComponent<Renderer>();

        if (cube != null) idle = cube.material.color;

        // Снимок мог прийти раньше Start, когда гасить было ещё нечего.
        Lit(!Locked());
    }

    // on - кнопка свободна, off - занята своим запросом.
    private void Lit(bool on)
    {
        if (cube == null) return;

        cube.material.color = on ? idle : new Color(idle.r * 0.25f, idle.g * 0.25f, idle.b * 0.25f, idle.a);
    }

    // Размеры пачек одного нажатия: сперва четыре адреса, следом два.
    private const int First = 4;
    private const int Second = 2;

    // Коробки обеих пачек подряд: [0, First) - первая, [First, First + Second) - вторая. Места второй
    // заведены сразу, но заполняются только когда она уходит.
    private DataList pack;

    // Адреса в том же порядке - для вывода.
    private string[] asks = new string[0];

    // Какие тела уже выведены строкой response - чтобы не печатать одно и то же каждый кадр.
    private bool[] seen = new bool[0];

    // Ушла ли вторая пачка. До закрытия первой её нет вовсе - ни в очереди, ни на доске.
    private bool secondSent;

    // Выведен ли vresponse пачки. Каждая пачка закрывается сама, в свой момент.
    private bool closedFirst;
    private bool closedSecond;

    // Сколько тел каждой пачки уже приехало. Меньше размера - пачка частичная.
    private int gotFirst;
    private int gotSecond;

    // Момент vrequest КАЖДОЙ пачки. От него считается время её response и vresponse - поэтому
    // переписывает их только выпуск своей пачки, больше никто.
    private float sentFirst;
    private float sentSecond;

    // Часы сторожа. Отдельные: сторож перезапускает ожидание, и если бы он двигал время vrequest,
    // все следующие времена считались бы уже от последнего перезапроса.
    private float waitedFrom;

    private string log = "";

    // Снимок доски для тех, кто зашёл позже: лог и строка прогресса пачек, пустой прогресс - кнопка
    // свободна. Пишет только владелец и только в узлах - нажатие, закрытие, вход игрока, смена
    // владельца: живые игроки считают доску сами, снимок нужен лишь опоздавшим.
    [UdonSynced] private string syncedLog = "";
    [UdonSynced] private string syncedProgress = "";

    // Считает ли этот игрок доску сам. До первого Press он зритель и показывает снимок владельца,
    // после - снимок игнорирует: чужая доска перетирала бы свою.
    private bool live;

    // Сколько отказов клиента мы уже показали. Считаем их, а НЕ сравниваем текст: сервер лежит, и
    // вторая попытка падает дословно тем же сообщением - по строке она неотличима от старой, и риг
    // оставался ждать тело, которое уже не приедет.
    private int said;

    // Сколько ждём свои тела, прежде чем попросить адреса заново. Больше одной паузы SDK, чтобы не
    // дёргать сервер на ровном месте, но заметно меньше, чем терпение человека у куба.
    private const float Patience = 15f;

    private void Update()
    {
        Walk();
        Wait();
    }

    private void Walk()
    {
        var player = Networking.LocalPlayer;

        if (player == null) return;

        Vector3 now = player.GetPosition();

        if (!started) { was = now; started = true; return; }

        // По горизонтали: падение и прыжок - не шаги, а вертикаль даёт больше всего мусора.
        float moved = Vector2.Distance(new Vector2(was.x, was.z), new Vector2(now.x, now.z));

        was = now;

        // Порог отсекает дрожание позиции на месте: без него счётчик капает даже у стоящего игрока.
        if (moved < 0.01f) return;

        walked += moved;

        while (walked >= stepLength) { walked -= stepLength; steps++; }

        Show();
    }

    // Ждём тела по коробкам. Пусто - ещё едут: это норма, а не ошибка, сервер не держит игрока,
    // пока ходит наружу.
    private void Wait()
    {
        if (pack == null) return;

        // Отказ приходит ПОЗЖЕ нажатия: разбирается с адресом клиент кадром позже. Молчать об этом
        // нельзя - выглядит как «нажал, и ничего».
        if (client.Errors != said)
        {
            said = client.Errors;
            pack = null;

            log = log + "\nrefused: " + client.LastError;

            Lit(true);
            Show();
            Snapshot();
            return;
        }

        bool changed = false;

        // response - на каждое тело, как только оно пришло. Время - от vrequest СВОЕЙ пачки.
        int bound = secondSent ? pack.Count : First;

        for (int i = 0; i < bound; i++)
        {
            if (seen[i]) continue;

            string body = Body(i);

            if (body == "") continue;

            seen[i] = true;
            changed = true;

            log = log + "\nresponse: " + asks[i] + ", " + body.Length + " bytes, " + Queries(i) + " queries, " + Ms(i < First ? sentFirst : sentSecond) + " ms";
        }

        int readyFirst = Ready(0, First);
        int readySecond = secondSent ? Ready(First, First + Second) : 0;

        // Первая пачка закрылась - печатаем её vresponse и ТОЛЬКО ТЕПЕРЬ выпускаем вторую.
        if (!closedFirst && readyFirst == First)
        {
            closedFirst = true;
            changed = true;

            log = log + "\nvresponse: полная " + First + "/" + First + ", " + Bytes(0, First) + " bytes, " + Ms(sentFirst) + " ms";

            SendSecond();
        }

        if (secondSent && !closedSecond && readySecond == Second)
        {
            closedSecond = true;
            changed = true;

            log = log + "\nvresponse: полная " + Second + "/" + Second + ", " + Bytes(First, First + Second) + " bytes, " + Ms(sentSecond) + " ms";
        }

        if (readyFirst != gotFirst || readySecond != gotSecond)
        {
            gotFirst = readyFirst;
            gotSecond = readySecond;

            changed = true;
        }

        if (closedFirst && closedSecond)
        {
            log = log + "\n" + Cut(Body(0));

            pack = null;

            Lit(true);
            Show();
            Snapshot();
            return;
        }

        if (changed) Show();

        if (client.Busy()) return;

        // Сторож: чужая пачка может увести наш адрес с собой, и тогда тело уедет в её набор, а мы
        // останемся ждать вечно. Ждём разумный срок и просим недостающее заново - по одному Result
        // на каждую пачку, где что-то пропало.
        if (Time.time - waitedFrom < Patience) return;

        waitedFrom = Time.time;

        Again(0, First);

        if (secondSent) Again(First, First + Second);
    }

    // Выпуск второй пачки: два Require, затем ОДИН Result. Её vrequest и её часы - отсюда.
    private void SendSecond()
    {
        int total = First + Second;

        string line = "\nvrequest 2 ->";

        for (int i = First; i < total; i++) line = line + (i == First ? " " : ", ") + asks[i];

        log = log + line;

        sentSecond = Time.time;
        waitedFrom = sentSecond;
        secondSent = true;

        for (int i = First; i < total; i++) Put(i, asks[i]);

        Release(total - 1);
    }

    // Время от момента from до сейчас.
    private int Ms(float from) => (int)((Time.time - from) * 1000f);

    // Сколько запросов клиент потратил на адрес i-й коробки: те, что его просили, и тот, что
    // привёз тело долгом. Клиент кладёт это в третий слот коробки.
    private int Queries(int at)
    {
        if (!pack.TryGetValue(at, out DataToken box) || box.TokenType != TokenType.DataList) return 0;

        return box.DataList.TryGetValue(2, out DataToken count) && count.TokenType == TokenType.Int ? count.Int : 0;
    }

    // Сколько коробок из [from, to) уже с телом.
    private int Ready(int from, int to)
    {
        int count = 0;

        for (int i = from; i < to; i++)
            if (Body(i) != "") count++;

        return count;
    }

    private int Bytes(int from, int to)
    {
        int total = 0;

        for (int i = from; i < to; i++) total += Body(i).Length;

        return total;
    }

    // Перепросить пустые коробки из [from, to) и выпустить их одним Result.
    private void Again(int from, int to)
    {
        int last = -1;

        for (int i = from; i < to; i++)
            if (Body(i) == "") { Put(i, asks[i]); last = i; }

        if (last >= 0) Release(last);
    }

    // Тело i-й коробки или пусто. Читаем слот напрямую, МИМО Result: Result - это сигнал отправки,
    // и звать его на каждом кадре ради проверки было бы неправдой о том, сколько раз мы просим.
    private string Body(int at)
    {
        if (!pack.TryGetValue(at, out DataToken box) || box.TokenType != TokenType.DataList) return "";

        return box.DataList.TryGetValue(0, out DataToken body) && body.TokenType == TokenType.String ? body.String : "";
    }

    // Один Result на пачку: он и выпускает её целиком.
    private void Release(int at)
    {
        if (pack.TryGetValue(at, out DataToken box) && box.TokenType == TokenType.DataList) client.Result(box.DataList);
    }

    // Просит адрес и кладёт его коробку на место at.
    //
    // Коробку чистим: на повторный адрес Require отдаёт ТУ ЖЕ самую, а в ней лежит тело с прошлого
    // раза - и Wait принял бы его за новый ответ в тот же кадр, с временем 0 мс.
    private void Put(int at, string url)
    {
        DataList box = client.Require(url);

        if (box == null) return;

        if (box.Count > 0) box.SetValue(0, "");

        pack.SetValue(at, new DataToken(box));
    }

    // Нажатие рассылается всем, нажавшему тоже. Решает нажавший: его кнопка занята - не шлём никому,
    // иначе у свободных игроков пачка ушла бы, а у него нет, и доски разошлись бы.
    //
    // Номер берём с шагов НАЖАВШЕГО и везём в событии: счётчик у каждого игрока свой, и без этого у
    // всех ушли бы разные адреса.
    public override void Interact()
    {
        if (client == null) { log = "client is not assigned"; Show(); return; }

        if (Locked())
        {
            Debug.Log("[SampleSteps] занято: ждём ответы");
            return;
        }

        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(Press), steps);

        // Счётчик обнуляем сразу: пока ответы едут, игрок уже шагает дальше, и эти шаги пойдут в
        // следующий запрос. Иначе одни и те же шаги уехали бы дважды.
        steps = 0;
        walked = 0f;

        Show();
    }

    // Логика нажатия. Приходит каждому игроку; pressedSteps - число шагов того, кто нажал.
    [NetworkCallable]
    public void Press(int pressedSteps)
    {
        if (client == null) { log = "client is not assigned"; Show(); return; }

        // Ждём своё - второе нажатие игнорируем. Иначе оно подменит коробки, и тела первых пачек
        // приедут в никуда: следим-то мы уже за другими.
        //
        // Молча выходить нельзя: снаружи это ровно то же, что мёртвая кнопка. Куб к этому моменту
        // уже потушен, а в лог кладём строку - чтобы было видно и тому, кто смотрит в консоль.
        if (pack != null)
        {
            Debug.Log("[SampleSteps] занято: ждём ответы");
            return;
        }

        // С этого нажатия доску считаем сами, снимок владельца больше не нужен.
        live = true;

        // Число приходит по сети, и прислать его может кто угодно: отрицательное в адрес не пускаем.
        if (pressedSteps < 0) pressedSteps = 0;

        int total = First + Second;

        pack = new DataList();
        asks = new string[total];
        seen = new bool[total];
        secondSent = false;
        closedFirst = false;
        closedSecond = false;
        gotFirst = 0;
        gotSecond = 0;

        for (int i = 0; i < total; i++)
        {
            asks[i] = urlPrefix + (pressedSteps + i);

            pack.Add(new DataToken(""));
        }

        log = "vrequest 4 ->";

        for (int i = 0; i < First; i++) log = log + (i == 0 ? " " : ", ") + asks[i];

        sentFirst = Time.time;
        waitedFrom = sentFirst;

        // Снимок счётчика ДО запроса: чужие отказы, случившиеся до нашего нажатия, не наши.
        said = client.Errors;

        // Первая пачка: четыре Require, затем ОДИН Result. Вторая уйдёт, когда эта закроется.
        for (int i = 0; i < First; i++) Put(i, asks[i]);

        Release(First - 1);

        Lit(false);

        Show();
        Snapshot();
    }

    // Занята ли кнопка: у живого - своей пачкой, у зрителя - пачкой владельца по снимку.
    private bool Locked() => live ? pack != null : syncedProgress != "";

    // Кладёт доску в снимок. Сериализует только владелец, остальным звать бесполезно.
    //
    // Зритель, ставший владельцем, когда прежний ушёл, отдаёт лог как есть, а кнопку - свободной:
    // чужой прогресс он не считает, и снять занятость потом было бы некому.
    private void Snapshot()
    {
        if (!Networking.IsOwner(gameObject)) return;

        syncedLog = log;
        syncedProgress = pack == null ? "" : Progress();

        RequestSerialization();
    }

    public override void OnDeserialization()
    {
        if (live) return;

        log = syncedLog;

        Lit(!Locked());
        Show();
    }

    // Вошедший получает доску на сейчас, а не на последний узел: вход посреди пачки иначе показал бы
    // её начало.
    public override void OnPlayerJoined(VRCPlayerApi player) => Snapshot();

    public override void OnOwnershipTransferred(VRCPlayerApi player) => Snapshot();

    // Владение по запросу не отдаём: снимок пишет владелец, и перехвативший его подсунул бы опоздавшим
    // чужую доску. Ушедшего владельца VRChat заменяет сам, мимо запроса.
    public override bool OnOwnershipRequest(VRCPlayerApi requestingPlayer, VRCPlayerApi requestedOwner) => false;

    private string Cut(string body)
    {
        string flat = body.Replace("\n", " ").Replace("\r", " ");

        return flat.Length <= 200 ? flat : flat.Substring(0, 200) + "...";
    }

    private string State(int got, int size) =>
        got == size ? "полная " + got + "/" + size : got == 0 ? "ждём 0/" + size : "частичная " + got + "/" + size;

    private string Progress() =>
        "пачка 4: " + State(gotFirst, First) + "   пачка 2: " + (secondSent ? State(gotSecond, Second) : "после первой");

    private void Show()
    {
        if (output == null) return;

        // Пока ждём своё - так и пишем, по каждой пачке отдельно. Молчащая доска неотличима от
        // сломанной. Зритель пишет прогресс владельца из снимка.
        string tail = pack != null ? Progress()
            : Locked() ? syncedProgress
            : steps + " steps - press to send";

        output.text = (log == "" ? "" : log + "\n\n") + tail;
    }
}
