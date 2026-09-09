using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

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

    [Tooltip("Optional: status is written here")]
    public Text output;

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
    private const int StepLevels = 1;
    private const int StepHyperLevels = 2;
    private const int StepPartialBig = 3;
    private const int StepHyperFull = 4;
    private const int StepHyperPrefix = 5;
    private const int StepLearn = 6;
    private const int StepLearned = 7;
    private const int StepHyperLearned = 8;

    // Один и тот же адрес двумя дорогами: сборкой и напрямую. Шаг 10 показывает частичное
    // покрытие (post/1 словарём не берётся и едет буквами), шаг 11 - ту же строку через /d/.
    private const int StepCombine = 9;
    private const int StepDirect = 10;

    // Пачка: три адреса копятся и уходят одним прогоном. Наружу приходит одно событие, а тела
    // разбираются по адресам - так это и будет работать у потребителя тулзы.
    private const int StepBatch = 11;

    private bool ready;
    private bool awaiting;
    private bool running;
    private int step;
    private float startedAt;
    private string board = "";

    public override void Interact()
    {
        if (client == null) { Say("client is not assigned"); return; }
        if (awaiting) return;

        if (action == 0)
        {
            client.codeword = codeword;
            client.Connect();

            // Раньше здесь стояло безусловное «init sent», даже когда метод выходил молча
            // (busy или отказ) - и это выглядело как зависание. Теперь говорим, что произошло.
            awaiting = client.LastError == "";

            Say(awaiting ? "connect sent" : "connect refused: " + client.LastError);
            return;
        }

        if (action == 2)
        {
            client.codeword = codeword;
            client.Remember();

            awaiting = client.LastError == "";

            Say(awaiting ? "remember sent" : "remember refused: " + client.LastError);
            return;
        }

        if (!ready) { Say("run Connect first"); return; }

        if (running) { running = false; Say("run stopped"); return; }

        running = true;
        step = StepHyperDb;
        board = testUrlSeeded + "   " + NumberOf(testUrlSeeded.Length) + " chars   (hyper from db, never sent)\n"
              + testUrlFull + "   " + NumberOf(testUrlFull.Length) + " chars   (levels L1-L3)\n"
              + testUrlLearn + "   " + NumberOf(testUrlLearn.Length) + " chars   (infinite: learn, then reuse)\n"
              + testUrl + "   " + NumberOf(testUrl.Length) + " chars   (partial - post/1 is plain)\n\n";

        SendStep();
    }

    public void OnQueryDone()
    {
        awaiting = false;

        if (client.LastError != "")
        {
            running = false;

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

        int queries = step == StepBatch ? client.BatchQueries : client.LastQueries;

        string line = Pad(TitleOf(step), 52)
                    + Pad(NumberOf((int)((Time.time - startedAt) * 1000f)) + " ms", 10)
                    + Pad(NumberOf(queries) + (queries == 1 ? " query" : " queries"), 11)
                    + Pad(client.LastJump < 0 ? "no jump" : "jump " + NumberOf(client.LastJump), 10)
                    + Pad("urls " + NumberOf(client.LastUrls), 8)
                    + Pad("runes " + NumberOf(client.LastChunks), 9)
                    + Pad("L2 " + NumberOf(client.LastL2), 6)
                    + Pad("L3 " + NumberOf(client.LastL3), 6)
                    + "inf " + NumberOf(client.LastInfinite);

        board += line + "\n";
        step++;

        Note(line);
        Show("\n" + client.TakeForwardedBody());

        if (step <= StepBatch) { SendStep(); return; }

        running = false;

        Note("done");
    }

    // Сколько прыжков приехало из БД. В релизе - пусто: по этому числу и видно «до и после
    // персиста», а мир такие подробности показывать не должен.
    private string JumpsNote()
    {
#if CQ_RELEASE
        return "";
#else
        return "jumps from db " + NumberOf(client.SeedJumps);
#endif
    }

    private void SendStep()
    {
        if (step == StepHyperDb) client.Request(testUrlSeeded);
        else if (step == StepLevels) client.Request(testUrlFull);
        else if (step == StepHyperLevels) client.Request(testUrlFull);
        else if (step == StepPartialBig) client.Request(testUrlPartialBig);
        else if (step == StepHyperFull) client.Request(testUrlPartialBig);
        else if (step == StepHyperPrefix) client.Request(testUrlSameStart);
        else if (step == StepLearn) client.Request(testUrlLearn);
        else if (step == StepLearned) client.Request(testUrlLearned);
        else if (step == StepHyperLearned) client.Request(testUrlLearned);
        else if (step == StepCombine) client.Request(testUrl);
        else if (step == StepBatch)
        {
            client.Queue(testUrlSeeded);
            client.Queue(testUrlFull);
            client.Queue(testUrlPartialBig);
            client.Run();
        }
        else client.RequestDirect(testUrl);

        awaiting = true;
        startedAt = Time.time;

        Note(TitleOf(step) + "   sending " + client.LastUrl);
        Show(TitleOf(step) + "   sending...");
    }

    private string TitleOf(int at)
    {
        if (at == StepHyperDb) return "1  hyper from db          (carts/5, never sent) ";
        if (at == StepLevels) return "2  combine                (comments/1)       ";
        if (at == StepHyperLevels) return "3  hyper, one step        (comments/1)       ";
        if (at == StepPartialBig) return "4  combine, partial big   (products/12/comments)";
        if (at == StepHyperFull) return "5  hyper, three steps     (products/12/comments)";
        if (at == StepHyperPrefix) return "6  no hyper, near miss    (products/12/reviews) ";
        if (at == StepLearn) return "7  partial, learning      (limit=10&skip=20) ";
        if (at == StepLearned) return "8  partial, learned       (limit=10&skip=50) ";
        if (at == StepHyperLearned) return "9  hyper over learned     (limit=10&skip=50) ";
        if (at == StepCombine) return "10 combine, partial      (comments/post/1) ";
        if (at == StepDirect) return "11 direct                 (comments/post/1)";
        return "12 batch of 3            (carts/5 + comments/1 + products/12/comments)";
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
