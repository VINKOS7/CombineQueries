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

    // Порядок: сначала БЕЗ персиста, потом с ним.
    //
    // Шаги 3-4 - хайпер на ходу: цепочка заведена в этом же прогоне, дерево живёт в памяти сервера.
    // Шаг 3 прыгает по ней целиком, шаг 4 показывает промах на похожем адресе.
    //
    // Дальше 5-6 - хайпер из БД. Реконнект включает персист (hypers=on) и забывает свои прыжки,
    // а сервер отдаёт сидом цепочку, которую клиент не собирал вовсе - её завёл dev-сид. Адрес
    // уходит в два запроса С ПЕРВОГО РАЗА: так это увидит игрок, зашедший в мир впервые.
    //
    // Шаг 9 - хайпер поверх ОБУЧЕНИЯ: к этому моменту сервер выучил фрагменты запроса и цепочка
    // состоит из них. Прыжок схлопывает и её - кешируются не буквы, а выученные фрагменты.
    private const int StepLevels = 0;
    private const int StepPartialBig = 1;
    private const int StepHyperFull = 2;
    private const int StepHyperPrefix = 3;
    private const int StepRejoin = 4;
    private const int StepHyperDb = 5;
    private const int StepLearn = 6;
    private const int StepLearned = 7;
    private const int StepHyperLearned = 8;

    // Один и тот же адрес двумя дорогами: сборкой и напрямую. Шаг 10 показывает частичное
    // покрытие (post/1 словарём не берётся и едет буквами), шаг 11 - ту же строку через /d/.
    private const int StepCombine = 9;
    private const int StepDirect = 10;

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
        step = StepLevels;
        board = testUrlFull + "   " + NumberOf(testUrlFull.Length) + " chars   (levels L1-L3)\n"
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

        // Реконнект ничего не собирает: у него интересно ровно одно число - сколько прыжков
        // сервер вернул из БД. Следующий шаг пойдёт уже по ним.
        string line = step == StepRejoin
            ? TitleOf(step) + "   " + NumberOf((int)((Time.time - startedAt) * 1000f)) + " ms   " + JumpsNote()
            : TitleOf(step) + "   " + NumberOf((int)((Time.time - startedAt) * 1000f)) + " ms   "
              + NumberOf(client.LastQueries) + " queries   "
              + "runes " + NumberOf(client.LastChunks)
              + "  L2 " + NumberOf(client.LastL2)
              + "  L3 " + NumberOf(client.LastL3)
              + "  inf " + NumberOf(client.LastInfinite);

        board += line + "\n";
        step++;

        Note(line);
        Show("\n" + client.TakeForwardedBody());

        if (step <= StepDirect) { SendStep(); return; }

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
        if (step == StepLevels) client.Request(testUrlFull);
        else if (step == StepPartialBig) client.Request(testUrlPartialBig);
        else if (step == StepHyperFull) client.Request(testUrlPartialBig);
        else if (step == StepHyperPrefix) client.Request(testUrlSameStart);
        else if (step == StepRejoin) { client.ForgetJumps(); client.Remember(); }
        else if (step == StepHyperDb) client.Request(testUrlSeeded);
        else if (step == StepLearn) client.Request(testUrlLearn);
        else if (step == StepLearned) client.Request(testUrlLearned);
        else if (step == StepHyperLearned) client.Request(testUrlLearned);
        else if (step == StepCombine) client.Request(testUrl);
        else client.RequestDirect(testUrl);

        awaiting = true;
        startedAt = Time.time;

        Note(TitleOf(step) + "   sending " + client.LastUrl);
        Show(TitleOf(step) + "   sending...");
    }

    private string TitleOf(int at)
    {
        if (at == StepLevels) return "1  L1-L3 fragments        (comments/1)       ";
        if (at == StepPartialBig) return "2  partial big            (products/12/comments)";
        if (at == StepHyperFull) return "3  hyper, same run       (products/12/comments)";
        if (at == StepHyperPrefix) return "4  no hyper, near miss    (products/12/reviews) ";
        if (at == StepRejoin) return "5  rejoin, forget jumps   (client knows nothing) ";
        if (at == StepHyperDb) return "6  hyper from db          (carts/5, never sent) ";
        if (at == StepLearn) return "7  partial, learning      (limit=10&skip=20) ";
        if (at == StepLearned) return "8  partial, learned       (limit=10&skip=50) ";
        if (at == StepHyperLearned) return "9  hyper over learned     (limit=10&skip=50) ";
        if (at == StepCombine) return "10 combine, partial      (comments/post/1) ";
        return "11 direct                 (comments/post/1)";
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
