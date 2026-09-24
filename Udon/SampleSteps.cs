using Newtonsoft.Json;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Data;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class SampleSteps : UdonSharpBehaviour
{
    public CombineQueries client;

    // Снимок доски для всех: доска ведущего, чтобы каждый видел то же и её изменение. syncedRunning -
    // идёт ли прогон: по нему кнопка заперта у всех, а не только у ведущего. Пишет только владелец
    // объекта (ведущий), в узлах прогона - покадрово сеть VRChat не вывезет.
    [UdonSynced] private string syncedLog = "";
    [UdonSynced] private string syncedProgress = "";
    [UdonSynced] private bool syncedRunning;


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

    // Куб самой кнопки. Пока прогон в пути, он гаснет: заблокированная кнопка, которая выглядит
    // как обычная, неотличима от сломанной - жмёшь и не понимаешь, почему ничего не происходит.
    private Renderer cube;
    private Color idle;

    private void Start()
    {
        cube = GetComponent<Renderer>();

        if (cube != null) idle = cube.material.color;

        // Снимок мог прийти раньше Start, когда гасить было ещё нечего.
        Lit(!Blocked());
    }

    // on - кнопка свободна, off - идёт чей-то прогон.
    private void Lit(bool on)
    {
        if (cube == null) return;

        cube.material.color = on 
            ? idle 
            : new Color(idle.r * 0.25f, idle.g * 0.25f, idle.b * 0.25f, idle.a);
    }

    // Размеры пачек одного нажатия: сперва четыре адреса, следом два.
    private const int First = 4;
    private const int Second = 2;

    // Коробки обеих пачек подряд: [0, First) - первая, [First, First + Second) - вторая. Места второй
    // заведены сразу, но заполняются только когда она уходит.
    // Ключи пачки: Require отдаёт ключ, Result по нему же возвращает тело.
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

    // Ведёт ли доску этот игрок сам (он нажал). Не ведущий - зритель: доску берёт из снимка и не
    // считает свою, иначе она перетёрла бы экран ведущего. Сбрасывается по концу прогона - тогда
    // следующий ведущий покажет уже свою.
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
    // пока ходит наружу. Крутится только у ведущего: у зрителя pack == null.
    private void Wait()
    {
        if (pack == null) return;

        // Отказ приходит ПОЗЖЕ нажатия: разбирается с адресом клиент кадром позже. Молчать об этом
        // нельзя - выглядит как «нажал, и ничего».
        if (client.Errors != said)
        {
            said = client.Errors;
            pack = null;
            live = false;

            log = log + "\nrefused: " + client.LastError;

            Lit(true);
            Show();
            Snapshot();
            return;
        }

        bool changed = false;

        // Сперва шапка response - это ОДИН ответ сервера, - а под ней response-url на каждый адрес,
        // который в нём приехал. Раньше номер ответа повторялся в каждой строке, и выглядело так,
        // будто ответов было столько же, сколько тел. Время - от vrequest СВОЕЙ пачки.
        int bound = secondSent ? pack.Count : First;

        // Потолок обязателен, и вот почему. Выход из цикла держится на том, что каждый проход
        // пометит хотя бы одну коробку, а это верно, только если Global(pick) не изменится между
        // выбором и печатью. Но Body() по дороге зовёт client.Result(), то есть Run(), - и поручиться
        // за неизменность номера нельзя. В Udon цена ошибки не исключение, а повисший главный поток:
        // редактор перестаёт выходить из плеймода, и помогает только убийство процесса.
        //
        // Проходов физически не может быть больше, чем коробок: каждый закрывает хотя бы одну.
        for (int pass = 0; pass < bound; pass++)
        {
            // Самый ранний из непоказанных ответов: тела одного ответа делят его номер.
            int pick = -1;

            for (int i = 0; i < bound; i++)
            {
                if (seen[i] || Body(i) == "") continue;

                if (pick < 0 || Global(i) < Global(pick)) pick = i;
            }

            if (pick < 0) break;

            int answer = Global(pick);
            int urls = 0, bytes = 0;

            for (int i = 0; i < bound; i++)
            {
                if (seen[i] || Body(i) == "" || Global(i) != answer) continue;

                urls++;
                bytes += Body(i).Length;
            }

            log = log + "\nresponse " + answer + ": " + urls + " urls, " + bytes + " bytes, "
                + Ms(pick < First ? sentFirst : sentSecond) + " ms";

            // Вторая цифра - номер ОТВЕТА внутри своего vrequest. Номер самого ответа уже в шапке, и
            // повторять его в каждой строке значило бы писать всем одно и то же: тела одного ответа
            // делят его номер, и строки выглядели бы как 10.1, 10.1 - будто шапки не хватает.
            for (int i = 0; i < bound; i++)
            {
                if (seen[i] || Body(i) == "" || Global(i) != answer) continue;

                seen[i] = true;
                changed = true;

                string body = Body(i);

                log = log + "\nresponse-url " + answer + "." + Answer(i) + ": " + asks[i] + ", " + body.Length
                    + " bytes, " + Queries(i) + " queries, " + client.StatusName(Key(i))
                    + ", " + Ms(i < First ? sentFirst : sentSecond) + " ms   " + Cut(body, 125);
            }
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
            log = log + "\n" + Cut(Body(0), 200);

            pack = null;
            live = false;

            Lit(true);
            Show();
            Snapshot();
            return;
        }

        // Что-то сдвинулось - показываем себе и транслируем доску всем: это и есть «все видят
        // изменение». Узел синхронизации, не каждый кадр.
        if (changed) { Show(); Snapshot(); }

        // Пока у клиента что-то в полёте, переспрашивать нечего: ответ либо привезёт наше
        // тело, либо довеском долг. Без этой строки сторож переспрашивает поверх летящего и
        // сам же держит полёт непустым.
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
        // Границы второй пачки это [First, First + Second), поэтому и предел обхода такой же.
        // Было Second, то есть 2 при First = 4: оба обхода не делали НИ ОДНОГО шага - строка
        // уходила пустой, адреса не просились вовсе, и пачку выпускал только сторож, через
        // Patience. Отсюда и её 20 с на первое тело.
        int total = First + Second;

        string line = "\nvrequest 2 ->";

        for (int i = First; i < total; i++) line = line + (i == First ? " " : ", ") + asks[i];

        log = log + line;

        sentSecond = Time.time;
        waitedFrom = sentSecond;
        secondSent = true;

        Debug.Log("SendSecond");
        for (int i = First; i < total; i++) Put(i, asks[i]);

        Release(total - 1);
    }

    // Время от момента from до сейчас.
    private int Ms(float from) => (int)((Time.time - from) * 1000f);

    // Во что обошлась ОТПРАВКА адреса i-й коробки: куски сборки с хвостом, либо один запрос, если
    // адрес назвал прыжок или голова. Клиент кладёт это в четвёртый слот.
    private int Queries(int at) => client.QueriesOf(Key(at));

    // Номер запроса своего vrequest, в ответе которого приехало тело: ответ на сам vrequest первый.
    private int Answer(int at) => client.AnswerOf(Key(at));

    // Тот же ответ, но номером с начала работы клиента - место в общем потоке запросов.
    private int Global(int at) => client.GlobalOf(Key(at));

    private string Key(int at)
    {
        if (pack == null || !pack.TryGetValue(at, out DataToken key) || key.TokenType != TokenType.String) return "";

        return key.String;
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
    // Метку ставим перед КАЖДЫМ Result: он же и выпускает набор, а клиент общий - без метки строки
    // лестницы и стенда лягут в консоль вперемешку и неразличимо.
    private string Body(int at)
    {
        client.who = "steps";

        return client.Result(Key(at));
    }

    // Один Result на пачку: он и выпускает её целиком.
    private void Release(int at)
    {
        client.who = "steps";

        client.Result(Key(at));
    }

    // Просит адрес и кладёт его коробку на место at.
    //
    // Коробку чистим: на повторный адрес Require отдаёт ТУ ЖЕ самую, а в ней лежит тело с прошлого
    // раза - и Wait принял бы его за новый ответ в тот же кадр, с временем 0 мс.
    private void Put(int at, string url)
    {
        pack.SetValue(at, client.Require(url));
    }

    // Нажатие ведёт САМ нажавший, локально. Пока идёт чей-то прогон - заперто у всех: нажать можно
    // только после того, как он отработал.
    public override void Interact()
    {
        if (client == null) { log = "client is not assigned"; Show(); return; }

        if (Blocked())
        {
            Debug.Log("[SampleSteps] занято: идёт прогон, дождись конца");
            return;
        }

        // Ведёт нажавший: забираем объект, чтобы наша доска ушла всем, и гоним по СВОИМ шагам.
        if (Networking.LocalPlayer != null) Networking.SetOwner(Networking.LocalPlayer, gameObject);

        live = true;

        BeginRun(steps);

        // Счётчик обнуляем сразу: пока ответы едут, игрок уже шагает дальше, и эти шаги пойдут в
        // следующий запрос. Иначе одни и те же шаги уехали бы дважды.
        steps = 0;
        walked = 0f;

        Show();
    }

    // Заводит прогон по числу шагов нажавшего. Первая пачка: четыре Require, затем ОДИН Result.
    private void BeginRun(int pressedSteps)
    {
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

        for (int i = 0; i < First; i++) Put(i, asks[i]);

        Release(First - 1);

        Lit(false);

        Show();
        Snapshot();
    }

    // Заперто у всех, пока идёт прогон: у ведущего - своей пачкой, у зрителя - по снимку.
    private bool Blocked() => pack != null || syncedRunning;

    // Кладёт доску в снимок - доску ведущего, чтобы все видели то же и её изменение. Только владелец
    // объекта: сериализует он. Зритель сюда и не попадает - у него pack == null и владения нет.
    private void Snapshot()
    {
        if (!Networking.IsOwner(gameObject)) return;

        syncedLog = log;
        syncedProgress = pack == null ? "" : Progress();
        syncedRunning = pack != null;

        RequestSerialization();
    }

    public override void OnDeserialization()
    {
        // Ведущий считает доску сам, снимок он не применяет - это его же эхо.
        if (live) return;

        log = syncedLog;

        Lit(!Blocked());
        Show();
    }

    // Вошедший получает доску на сейчас, а не на последний узел: вход посреди пачки иначе показал бы
    // её начало.
    public override void OnPlayerJoined(VRCPlayerApi player) => Snapshot();

    public override void OnOwnershipTransferred(VRCPlayerApi player) => Snapshot();

    // Начало тела одной строкой. Переводы строк плющим: иначе один ответ разъезжается по
    // канвасу на десяток строк и прячет всё остальное. Ширина доводом - у строки адреса своя.
    private string Cut(string body, int width)
    {
        string flat = body.Replace("\n", " ").Replace("\r", " ");

        return flat.Length <= width ? flat : flat.Substring(0, width) + "...";
    }

    private string State(int got, int size) =>
        got == size ? "полная " + got + "/" + size : got == 0 ? "ждём 0/" + size : "частичная " + got + "/" + size;

    private string Progress() =>
        "пачка 4: " + State(gotFirst, First) + "   пачка 2: " + (secondSent ? State(gotSecond, Second) : "после первой");

    private void Show()
    {
        if (output == null) return;

        // Ведущий пишет свой прогресс; зритель во время чужого прогона - прогресс из снимка; вне
        // прогона - своё число шагов. Молчащая доска неотличима от сломанной.
        string tail = pack != null ? Progress()
            : Blocked() ? syncedProgress
            : steps + " steps - press to send";

        output.text = (log == "" ? "" : log + "\n\n") + tail;
    }
}
