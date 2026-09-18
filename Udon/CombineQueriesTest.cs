using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Data;
using VRC.SDKBase;

// Чёрный куб (Connect) закреплён за первым нажавшим - для остальных заперт, только он жмёт его снова.
// Зелёный куб (прогон) и красный (шаги) работают как раньше, локально у нажавшего. Ничего, кроме
// закрепления чёрного, тут не синхронизируется.
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class CombineQueriesTest : UdonSharpBehaviour
{
    public CombineQueries client;

    [Tooltip("0 = Connect, 1 = run the comparison, 2 = Remember (повторное подключение дельтой)")]
    public int action = 0;

    [Tooltip("Codeword the server expects (Auth:Codeword); empty in dev")]
    public string codeword = "";

    // code-only, not serialized - a scene value cannot override these and desync the labels
    //
    // Порядок: сперва уровни словаря, потом обучение двумя заходами, и только в конце частичное
    // покрытие с прямой отправкой - они самые дорогие.
    private string testUrlFull = "https://dummyjson.com/comments/1";

    // Два РАЗНЫХ url с общей длинной частью: на первом заходе сервер учит подстроку, на втором она
    // должна адресоваться фрагментом. Строка, которой не хватило трёхмерного пространства
    // (dfaSize x pageCount), берётся заёмом - якорь плюс сдвиг, всё те же два запроса.
    // Partial с большим покрытием: два куска легли фрагментами (адреса 16 и 115, оба L3), а один
    // не нашёлся и ушёл руной. Ровно тот случай, ради которого partial и отличают от полного:
    // словарь сработал почти везде, но «почти» стоит лишнего запроса.
    private string testUrlPartialBig = "https://dummyjson.com/products/12/comments";

    // Промах хайпера: общее НАЧАЛО с предыдущим адресом, но не он сам. Клиент держит плоский
    // словарь «адрес -> прыжок», а не дерево, поэтому похожий адрес прыжка не даёт и собирается
    // целиком. Это осознанная цена: дерево осталось на сервере, фронт от него избавлен.
    private string testUrlSameStart = "https://dummyjson.com/products/12/reviews";

    // Адрес, который клиент НИКОГДА не собирал: его цепочку сервер завёл сам (dev-сид) и держит
    // в БД. Приезжает прыжком в connect - значит два запроса с первого раза, без разогрева.
    private string testUrlSeeded = "https://dummyjson.com/carts/5";

    private string testUrlLearn = "https://dummyjson.com/products?limit=10&skip=20";
    private string testUrlLearned = "https://dummyjson.com/products?limit=10&skip=50";

    private string testUrl = "https://dummyjson.com/comments/post/1";

    // ПОСЕЯННЫЕ: сервер знает их с миграции. Семья корня идёт номерами 900-903, семья carts - с
    // 904, и рядом с ней лежит carts/5. Из них и берутся гиперы.
    private string testUrlComments = "https://dummyjson.com/comments";
    private string testUrlProducts = "https://dummyjson.com/products";
    private string testUrlRecipes = "https://dummyjson.com/recipes";
    private string testUrlQuotes = "https://dummyjson.com/quotes";

    // НЕПОСЕЯННЫЕ: этих не знает никто, они и уедут сборкой. Все ПОЛНОФРАГМЕНТНЫЕ - начало
    // накрывается одним куском словаря, а цифра уезжает хвостом, - поэтому каждый стоит ровно два
    // запроса. Дешевле сборки не бывает, а лестнице нужны именно дешёвые.
    private string testUrlCart6 = "https://dummyjson.com/carts/6";
    private string testUrlTodo1 = "https://dummyjson.com/todos/1";
    private string testUrlTodo2 = "https://dummyjson.com/todos/2";
    private string testUrlUser1 = "https://dummyjson.com/users/1";
    private string testUrlUser2 = "https://dummyjson.com/users/2";
    private string testUrlUser3 = "https://dummyjson.com/users/3";
    private string testUrlPost1 = "https://dummyjson.com/posts/1";
    private string testUrlPost2 = "https://dummyjson.com/posts/2";
    private string testUrlUser4 = "https://dummyjson.com/users/4";
    private string testUrlUser5 = "https://dummyjson.com/users/5";

    [Tooltip("Optional: status is written here")]
    public Text output;

    [Tooltip("Панель ушедших vrequest'ов")]
    public Text requests;

    [Tooltip("Панель ответов: response и vresponse")]
    public Text responses;

    [Tooltip("Панель тел: чей набор, адрес и что пришло")]
    public Text data;

    // ПЕРВЫМ идёт хайпер из БД: сид приезжает вместе с connect, поэтому адрес, которого клиент не
    // собирал ни разу, уходит в два запроса сразу - /h/ поднимает всю combine-часть, /t/ закрывает.
    // Реконнект для этого не нужен, знание уже на руках; поэтому шага «забыть прыжки» тут и нет.
    //
    // Дальше - парами: сборка, следом тот же адрес прыжком. Видно, что именно экономится.
    // Пара 2-3 - цепочка из ОДНОГО шага: 2 запроса против 2, прыжок не выигрывает ничего.
    // Пара 4-5 - цепочка из трёх: 4 против 2.
    // Шаг 6 - промах: похожий адрес прыжка не даёт, у клиента плоский словарь, а не дерево.
    // Шаг 9 - прыжок поверх ОБУЧЕНИЯ: цепочка собрана из выученных фрагментов, кешируются они.
    private const int StepHyperDb = 0;

    // ЛЕСТНИЦА ГОЛОВЫ идёт сразу за первым прыжком - на ней демка и держится.
    //
    // Каждый шаг это пачка из четырёх, и с каждым шагом гиперов в ней на один меньше, а сборок на
    // одну больше: 3+1, 2+2, 1+3, 0+4. Видно, во что упирается цена - не в число адресов, а в то,
    // сколько из них сервер уже знает.
    //
    // В каждом шаге, кроме последнего, один адрес сервер знает, а клиент нет - его и находит
    // голова. Забываем его так, чтобы диапазон не подобрал его заодно: у соседа по семье номер
    // должен быть МЕНЬШЕ забытого, иначе прыжок вернёт его сам и голову звать станет не за чем.
    // Последние три ступени меняют не число гиперов, а ЦЕНУ четвёртого адреса: сперва он
    // полнофрагментный, потом наполовину рунный, потом прямой. Замыкает лестницу пачка, где
    // прямые все - это потолок, дороже уже не бывает.
    private const int StepHead3 = 1;
    private const int StepHead2 = 2;
    private const int StepHead1 = 3;
    private const int StepHeadPartial = 4;
    private const int StepHeadDirect = 5;
    private const int StepAllDirect = 6;

    // Сразу за лестницей - ОДИН прямой запрос, для сравнения: вот столько стоит адрес, если не
    // помогает ни словарь, ни память сервера.
    private const int StepDirectOne = 7;

    private const int StepLevels = 8;
    private const int StepHyperLevels = 9;
    private const int StepPartialBig = 10;
    private const int StepHyperFull = 11;
    private const int StepHyperPrefix = 12;
    private const int StepLearn = 13;
    private const int StepLearned = 14;
    private const int StepHyperLearned = 15;

    // Тот же адрес сборкой - пара к прямому запросу выше: частичное покрытие, post/1 словарём не
    // берётся и едет буквами.
    private const int StepCombine = 16;

    // Пачка: три адреса копятся и уходят одним прогоном. Наружу приходит одно событие, а тела
    // разбираются по адресам - так это и будет работать у потребителя тулзы.
    private const int StepBatch = 17;

    private bool ready;
    private bool awaiting;
    private bool running;
    private int step;
    private float startedAt;
    private string board = "";

    // Куб кнопки. Пока прогон идёт (зелёный) или чёрный закреплён за другим - гаснет и нажатий не
    // принимает: заблокированная кнопка, которая выглядит как обычная, неотличима от сломанной.
    private Renderer cube;
    private Color idle;

    private void Start()
    {
        cube = GetComponent<Renderer>();

        if (cube != null) idle = cube.material.color;

        // Закрепление чёрного могло прийти раньше Start, когда гасить было ещё нечего.
        Lit(!Blocked());
    }

    // on - кнопка свободна, off - заблокирована. Чёрный куб темнее не станет, поэтому тёмный вместо
    // гашения становится серым - иначе блок не виден.
    private void Lit(bool on)
    {
        if (cube == null || action == 2) return;

        Color off = idle.r + idle.g + idle.b < 0.3f
            ? new Color(0.35f, 0.35f, 0.35f, idle.a)
            : new Color(idle.r * 0.25f, idle.g * 0.25f, idle.b * 0.25f, idle.a);

        cube.material.color = on ? idle : off;
    }

    // Кодовое слово: поле сцены, а если оно пустое - то, с которым собран мир.
    //
    // Поле сериализовано, и билдер рига проставил его ОДИН РАЗ, в тот мод, что был тогда. Переключил
    // контур дефайнами - в сцене осталось старое, у дева пустое. Пустое слово это очередь букв
    // нулевой длины: клиент не шлёт ни одной /k/, сразу зовёт /kf, и сервер отвечает 403. Выглядит
    // как «слово не то», хотя слова не было вовсе.
    private string Word() => codeword == "" ? CombineQueriesEnvironment.Codeword : codeword;

    // Клиент больше ни о чём не уведомляет: наружу у него только Require и Result, события с целью
    // и подпиской убраны. Готовность видно по тому, что поток освободился, - её и ждём здесь.
    private void Update()
    {
        Guard();

        if (!awaiting || client == null || client.Busy()) return;

        OnQueryDone();
    }

    public override void Interact()
    {
        if (client == null) { Say("client is not assigned"); return; }

        // Чёрный куб (Connect/Remember): закреплён за первым нажавшим, для остальных заперт.
        if (action != 1)
        {
            if (ConnectLocked()) { Say("занято: чёрный куб закреплён за другим игроком"); return; }

            if (!ConnectTaken()) Claim();

            Act();
            return;
        }

        // Зелёный куб (прогон): локальный прогон нажавшего, как было.
        if (running) { Note("занято: идёт прогон, дождись done"); return; }

        if (awaiting && !client.Busy()) awaiting = false;

        if (awaiting) return;

        // Connect мог нажать другой куб - у клиента общее подключение. Спрашиваем сам клиент.
        if (!ready && client.Connected()) ready = true;

        if (!ready) { Say("run Connect first"); return; }

        // Нажатие после завершённого прогона - прогон заново, с чистыми досками.
        StartRun();
    }

    // Connect/Remember. Зовётся только у владельца чёрного куба - гейт стоит в Interact.
    private void Act()
    {
        // Кнопка не занята своим прошлым запросом - спрашиваем клиент: он не занят, значит прошлое
        // нажатие отработало, чем бы оно ни кончилось.
        if (awaiting && !client.Busy()) awaiting = false;

        if (awaiting) return;

        if (action == 0)
        {
            client.codeword = Word();
            client.Connect();

            // Раньше здесь стояло безусловное «init sent», даже когда метод выходил молча
            // (busy или отказ) - и это выглядело как зависание. Теперь говорим, что произошло.
            awaiting = client.LastError == "";

            Say(awaiting ? "connect sent" : "connect refused: " + client.LastError);
            return;
        }

        // action == 2
        client.codeword = Word();
        client.Remember();

        awaiting = client.LastError == "";

        Say(awaiting ? "remember sent" : "remember refused: " + client.LastError);
    }

    // Сколько vrequest выпускает один прогон. Считаем по журналу клиента, а не по шагам: шаг с
    // пачкой клиент может разбить на несколько наборов, и девять шагов дали бы больше девяти.
    private const int MaxVrequests = 9;

    // Сколько vrequest уже ушло в этом прогоне.
    private int vrequests;

    // Сколько раз mark встречается в text.
    private int Count(string text, string mark)
    {
        int count = 0;
        int at = text.IndexOf(mark);

        while (at >= 0) { count++; at = text.IndexOf(mark, at + mark.Length); }

        return count;
    }

    // Прогон с нуля: доски пустые, счётчик vrequest обнулён. Журналы клиента выпиваем впустую,
    // чтобы хвост прошлого прогона не лёг строками в новый.
    private void StartRun()
    {
        running = true;
        step = StepHyperDb;
        vrequests = 0;

        Lit(false);

        client.TakeRequests();
        client.TakeResponses();
        client.TakeData();

        if (output != null) output.text = "";
        if (requests != null) requests.text = "";
        if (responses != null) responses.text = "";
        if (data != null) data.text = "";

        board = testUrlSeeded + "   " + NumberOf(testUrlSeeded.Length) + " chars   (hyper from db, never sent)\n"
              + testUrlFull + "   " + NumberOf(testUrlFull.Length) + " chars   (levels L1-L3)\n"
              + testUrlLearn + "   " + NumberOf(testUrlLearn.Length) + " chars   (infinite: learn, then reuse)\n"
              + testUrl + "   " + NumberOf(testUrl.Length) + " chars   (partial - post/1 is plain)\n\n";

        SendStep();
    }

    // Private: по сети её звать нельзя, а public-метод без подчёркивания вызвал бы любой игрок.
    private void OnQueryDone()
    {
        awaiting = false;

        if (client.LastError != "")
        {
            running = false;

            Lit(true);

            Say("ERROR\n" + client.LastError);
            return;
        }

        if (!ready)
        {
            ready = true;

            Say("ready - touch the green cube");

            if (JumpsNote() != "") Note("connect: " + JumpsNote());

            return;
        }
        if (!running) return;

        bool packed = step == StepBatch || (step >= StepHead3 && step <= StepAllDirect);

        int queries = packed ? client.BatchQueries : client.LastQueries;

        // Заголовок шага короткий. Адреса и их судьба идут ПОД ним, теми же строками, что и в
        // консоли: vrequest / response / vresponse. Один рассказ, а не два.
        string line = Pad(TitleOf(step), 34)
                    + Pad(NumberOf((int)((Time.time - startedAt) * 1000f)) + " ms", 10)
                    + Pad(NumberOf(queries) + (queries == 1 ? " query" : " queries"), 11)
                    + Pad(RoadOf(), 17)
                    + Pad("urls " + NumberOf(client.LastUrls), 8)
                    + Pad("runes " + NumberOf(client.LastChunks), 9)
                    + Pad("L2 " + NumberOf(client.LastL2), 6)
                    + Pad("L3 " + NumberOf(client.LastL3), 6)
                    + "inf " + NumberOf(client.LastInfinite);

        board += line + "\n";

        // Что ушло за шаг - считаем vrequest прямо по журналу, прежде чем вылить его в панель.
        string sent = client.TakeRequests();

        vrequests += Count(sent, "vrequest:");

        // Ушедшее и пришедшее - в свои панели, теми же строками, что в консоли.
        Pour(requests, sent);
        Pour(responses, client.TakeResponses());
        Pour(data, client.TakeData());

        step++;

        // В консоль сводку шага НЕ пишем: там только request/vrequest и их ответы. Это тот же
        // самый запрос, о котором уже сказал клиент, и второй раз о нём читать незачем.
        Show("\n" + client.Take());

        // Прогон ограничен числом vrequest, а не шагов: набралось девять - останавливаемся. Шаг
        // посреди себя не режем, поэтому последний может немного перебрать, если его пачка ушла
        // несколькими наборами.
        if (vrequests < MaxVrequests && step <= StepBatch) { SendStep(); return; }

        running = false;

        Lit(true);

        Note("done, vrequests " + NumberOf(vrequests));
    }

    // Сколько прыжков приехало из БД. Показываем в обоих модах: prod пока отличается от dev
    // только полным персистом, и это как раз то число, по которому персист и виден.
    private string JumpsNote() => "jumps from db " + NumberOf(client.SeedJumps);

    private void SendStep()
    {
        asked = "";

        if (step == StepHyperDb) One(testUrlSeeded);
        else if (step == StepLevels) One(testUrlFull);
        else if (step == StepHyperLevels) One(testUrlFull);
        else if (step == StepPartialBig) One(testUrlPartialBig);
        else if (step == StepHyperFull) One(testUrlPartialBig);
        else if (step == StepHyperPrefix) One(testUrlSameStart);
        else if (step == StepLearn) One(testUrlLearn);
        else if (step == StepLearned) One(testUrlLearned);
        else if (step == StepHyperLearned) One(testUrlLearned);
        else if (step == StepCombine) One(testUrl);
        else if (step == StepBatch)
        {
            Ask(testUrlSeeded);
            Ask(testUrlFull);
            Ask(testUrlPartialBig);
            Release();
        }
        else if (step == StepHead3)
        {
            // 3 гипера + 1 сборка, и гиперы уходят ОДНИМ запросом: у comments, recipes и quotes
            // номера идут подряд, диапазон берёт их разом. Голову тут не зовём намеренно - она
            // стоила бы два лишних запроса, а показывать её есть где дальше.
            Ask(testUrlComments);
            Ask(testUrlRecipes);
            Ask(testUrlQuotes);
            Ask(testUrlCart6);
            Release();
        }
        else if (step == StepHead2)
        {
            // ЕДИНСТВЕННЫЙ шаг с головой - здесь она и показывается, за свою честную цену.
            //
            // comments сервер знает, а клиент нет: голова находит его по куску расхождения и даёт
            // номер (1 запрос), тело забирает прыжок (ещё 1). Забываем именно его: расхождение
            // «comments» есть в словаре, а у carts/2 расхождение это цифра, спрашивать нечем.
            //
            // Итого 2 гипера + 2 сборки = 1 + 1 + 4 = 6 запросов. Два из них - плата за голову,
            // и она окупается ровно тогда, когда адрес собрал кто-то другой.
            client.ForgetJump(testUrlComments);

            Ask(testUrlRecipes);
            Ask(testUrlComments);
            Ask(testUrlTodo1);
            Ask(testUrlTodo2);
            Release();
        }
        else if (step == StepHead1)
        {
            // 1 гипер + 3 сборки: 1 + 6 = 7 запросов. Гипер один, остальное платится сборкой -
            // это и есть цена незнакомых серверу адресов.
            Ask(testUrlProducts);
            Ask(testUrlUser1);
            Ask(testUrlUser2);
            Ask(testUrlUser3);
            Release();
        }
        else if (step == StepHeadPartial)
        {
            // 3 гипера одним запросом + адрес, накрытый словарём НАПОЛОВИНУ: остаток едет рунами,
            // и каждый чанк это отдельный запрос. Видно, во что обходится непокрытая часть.
            Ask(testUrlComments);
            Ask(testUrlRecipes);
            Ask(testUrlQuotes);
            Ask(testUrlSameStart);
            Release();
        }
        else if (step == StepHeadDirect)
        {
            // Те же 3 гипера одним запросом, но четвёртый идёт ПРЯМЫМ - мимо словаря целиком.
            // Пачке однородность не нужна: адрес помечается на входе и едет своей дорогой.
            Ask(testUrlComments);
            Ask(testUrlRecipes);
            Ask(testUrlQuotes);
            AskDirect(testUrl);
            Release();
        }
        else if (step == StepAllDirect)
        {
            // Потолок: четыре прямых. Ни словаря, ни памяти - каждый адрес диктуется побуквенно.
            AskDirect(testUrlUser4);
            AskDirect(testUrlUser5);
            AskDirect(testUrlPost1);
            AskDirect(testUrlPost2);
            Release();
        }
        else { Remember(testUrl + " direct"); client.RequestDirect(testUrl); }

        awaiting = true;
        startedAt = Time.time;

        // В лог о начале шага не пишем: следом клиент напечатает vrequest с настоящим составом
        // набора, а «шаг собирается просить» - это намерение, и оно только путает.
        Show(TitleOf(step) + "   sending...");
    }

    private string TitleOf(int at)
    {
        // Только НАЗВАНИЕ шага. Адреса в скобки дописываются после ответа - те, что реально
        // уехали: прыжок тащит семью, голова называет соседей, и заранее этого не знает никто.
        if (at == StepHyperDb) return "1  hyper persist";
        if (at == StepHead3) return "2  head: hyper x3 + 1 combine";
        if (at == StepHead2) return "3  head: found + hyper + 2 combine";
        if (at == StepHead1) return "4  head: hyper + 3 combine";
        if (at == StepHeadPartial) return "5  head: hyper x3 + 1 partial";
        if (at == StepHeadDirect) return "6  head: hyper x3 + 1 direct";
        if (at == StepAllDirect) return "7  head: all direct";
        if (at == StepDirectOne) return "8  direct, one";
        if (at == StepLevels) return "9  combine";
        if (at == StepHyperLevels) return "10 hyper, one step";
        if (at == StepPartialBig) return "11 combine, partial big";
        if (at == StepHyperFull) return "12 hyper, three steps";
        if (at == StepHyperPrefix) return "13 no hyper, near miss";
        if (at == StepLearn) return "14 partial, learning";
        if (at == StepLearned) return "15 partial, learned";
        if (at == StepHyperLearned) return "16 hyper over learned";
        if (at == StepCombine) return "17 combine, partial";
        return "18 batch of 3";
    }

    // Дорога адреса и номер прыжка одной колонкой. Раньше тут стояло «jump N / no jump», и по
    // нему не читалось главное: голова тоже стоит запрос, но в счётчике неотличима от сборки.
    // Теперь видно, чем кончилось - head/hyper (голова окупилась) или head/combine (впустую).
    private string RoadOf()
    {
        string road = client.LastRoad == "" ? "-" : client.LastRoad;

        return client.LastJump < 0 ? road : road + " " + NumberOf(client.LastJump);
    }

    // Что шаг попросил - строкой, ещё до отправки. Пачка знает свои адреса заранее, и в логе они
    // должны стоять СПИСКОМ, а не по одному.
    private string asked = "";

    private void One(string url)
    {
        Remember(url);

        client.Request(url);
    }

    // Стенд - такой же потребитель тулзы, как любой мир, поэтому просит ровно тем же, чем будут
    // просить снаружи: Require. Коробку он не читает - тела ему приходят журналами, - но ходить в
    // обход публичного входа стенду нельзя: то, что он показывает, должно быть воспроизводимо.
    private void Ask(string url)
    {
        Remember(url);

        keep = client.Require(url);
    }

    private void AskDirect(string url)
    {
        Remember(url + " direct");

        keep = client.RequireDirect(url);
    }

    // Коробка ПОСЛЕДНЕГО адреса шага. Нужна ровно для одного: по ней и выпускается набор.
    private DataList keep;

    // Отправка набора. Один Result на весь шаг - минимум, который вообще возможен: первый же вызов
    // выпускает всё, что набрано, и помечает набор отправленным. Тела стенд по-прежнему берёт
    // журналами, коробку он не читает.
    private void Release()
    {
        if (keep != null) client.Result(keep);
    }

    private void Remember(string url)
    {
        string tail = url;
        int cut = url.IndexOf("dummyjson.com/");

        if (cut >= 0) tail = url.Substring(cut + 14);

        asked = asked == "" ? tail : asked + ", " + tail;
    }

    // Колонки держим пробелами: строка идёт в один Text, и без выравнивания числа расползаются.
    private string Pad(string value, int width)
    {
        string padded = value;

        while (padded.Length < width) padded = padded + " ";

        return padded;
    }

    private string NumberOf(int value)
    {
        if (value <= 0) return "0";

        string digits = "";

        while (value > 0)
        {
            digits = "0123456789".Substring(value % 10, 1) + digits;
            value /= 10;
        }

        return digits;
    }

    // Дописывает строки в панель. Пусто - панели не трогаем, иначе она мигает пустотой на шагах,
    // где в эту сторону ничего не двигалось.
    private void Pour(Text panel, string lines)
    {
        if (panel == null || lines == "") return;

        panel.text = panel.text == "" ? lines : panel.text + "\n" + lines;
    }

    // Заблокирован ли КУБ для гашения: зелёный - идёт прогон; чёрный - закреплён за другим игроком.
    private bool Blocked()
    {
        if (action != 1) return ConnectLocked();

        return running;
    }

    // Кто первым нажал чёрный куб в этом инстансе. Жать дальше может только он, сколько угодно раз.
    // Это защита от перехвата подключения, а не вежливость: остальные кубы запираются лишь на время
    // своего vrequest, и кто их нажал - неважно.
    // -1 - ещё никто; свободным куб становится только в новом инстансе.
    [UdonSynced] private int connectOwner = -1;

    // Что сейчас показывает куб - чтобы не перекрашивать материал каждый кадр.
    private bool shownLocked;

    // Закрепление стоит до конца инстанса. Уход закрепившего его НЕ снимает: иначе перехватить куб
    // хватило бы выхода и возврата, а смысл закрепления в том, что подключение в инстансе одно.
    private bool ConnectTaken() => connectOwner >= 0;

    // Заблокирован для этого игрока: закреплён, и не за ним. Только чёрный куб.
    private bool ConnectLocked() => action != 1 && ConnectTaken() && (Networking.LocalPlayer == null || Networking.LocalPlayer.playerId != connectOwner);

    // Забирает чёрный куб себе: владение объектом, чтобы записать закрепление, и сразу рассылка.
    private void Claim()
    {
        if (Networking.LocalPlayer == null) return;

        Networking.SetOwner(Networking.LocalPlayer, gameObject);

        connectOwner = Networking.LocalPlayer.playerId;

        RequestSerialization();
    }

    private void Guard()
    {
        bool blocked = Blocked();

        if (blocked == shownLocked) return;

        shownLocked = blocked;

        Lit(!blocked);
    }

    // Закрепление чёрного приезжает синхронизацией - перекрашиваем куб под новое состояние.
    public override void OnDeserialization() => Guard();

    // Владение отдаём: чёрный куб забирает первый нажавший, чтобы записать закрепление.
    public override bool OnOwnershipRequest(VRCPlayerApi requestingPlayer, VRCPlayerApi requestedOwner) => true;

    private void Note(string line) => Debug.Log("[CombineQueriesTest] " + line);

    private void Show(string tail)
    {
        if (output != null) output.text = board + tail;
    }

    private void Say(string message)
    {
        Note(message);
        Show(message);
    }
}
