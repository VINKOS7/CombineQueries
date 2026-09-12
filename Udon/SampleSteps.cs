using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Data;
using VRC.SDKBase;

// РЕЛИЗНЫЙ РИГ: одна кнопка, и в ней весь смысл тулзы.
//
// Мир считает, сколько шагов прошёл игрок, и по нажатию идёт по адресу, в котором это число стоит
// прямо в пути. Такого адреса не существовало до нажатия, запечь его заранее нельзя - а Udon умеет
// грузить только запечённое. Поэтому кнопка и показывает то, чего в мире без тулзы не бывает.
//
// Риг НЕЗАВИСИМ от остальных: он не перехватывает событие клиента и не читает его журнал. Просит
// через Require, получает ключ и ждёт по нему результат - так же, как это будет делать любой мир,
// взявший тулзу. Паузы SDK, очередь и долг клиент разруливает сам.
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
    }

    // on - кнопка свободна, off - занята своим запросом.
    private void Lit(bool on)
    {
        if (cube == null) return;

        cube.material.color = on ? idle : new Color(idle.r * 0.25f, idle.g * 0.25f, idle.b * 0.25f, idle.a);
    }

    // Коробка результата: её отдаёт Require, и она же наполняется, когда тело приедет.
    private DataList result;

    private string asked = "";
    private float sentAt;
    private string log = "";

    // Сколько отказов клиента мы уже показали. Считаем их, а НЕ сравниваем текст: сервер лежит, и
    // вторая попытка падает дословно тем же сообщением - по строке она неотличима от старой, и риг
    // оставался ждать тело, которое уже не приедет.
    private int said;

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

    // Ждём результат по ключу. Пусто - ещё едет: это норма, а не ошибка, сервер не держит игрока,
    // пока ходит наружу.
    private void Wait()
    {
        if (result == null) return;

        // Отказ приходит ПОЗЖЕ нажатия: Require только кладёт адрес в очередь, а разбирается с ним
        // клиент кадром позже. Молчать об этом нельзя - выглядит как «нажал, и ничего».
        if (client.Errors != said)
        {
            said = client.Errors;
            result = null;

            log = log + "\nrefused: " + client.LastError;

            Lit(true);
            Show();
            return;
        }

        string body = client.Result(result);

        if (body == "") return;

        log = log + "\nvresponse: " + asked + "   " + body.Length + " bytes, "
            + (int)((Time.time - sentAt) * 1000f) + " ms\n" + Cut(body);

        result = null;

        Lit(true);
        Show();
    }

    public override void Interact()
    {
        if (client == null) { log = "client is not assigned"; Show(); return; }

        // Ждём своё - второе нажатие игнорируем. Иначе оно подменит коробку, и тело первого адреса
        // приедет в никуда: следим-то мы уже за другой. Ровно на этом и терялся ответ.
        //
        // Молча выходить нельзя: снаружи это ровно то же, что мёртвая кнопка. Куб к этому моменту
        // уже потушен, а в лог кладём строку - чтобы было видно и тому, кто смотрит в консоль.
        if (result != null)
        {
            Debug.Log("[SampleSteps] занято: ждём ответ на " + asked);
            return;
        }

        // Занят клиент или нет - не наше дело: Require положит адрес в очередь, и тулза отправит
        // его, как только сможет. Ждать и считать паузы SDK - её работа, не мира.
        asked = urlPrefix + steps;

        log = "vrequest -> " + asked;
        sentAt = Time.time;

        // Снимок счётчика ДО запроса: чужие отказы, случившиеся до нашего нажатия, не наши. Всё,
        // что натикает после, - уже про этот адрес, и об этом скажет Wait.
        said = client.Errors;

        result = client.Require(asked);

        // Гасим куб: с этого момента и до своего ответа кнопка занята.
        Lit(false);

        // Счётчик обнуляем сразу: пока ответ едет, игрок уже шагает дальше, и эти шаги пойдут в
        // следующий запрос. Иначе одни и те же шаги уехали бы дважды.
        steps = 0;
        walked = 0f;

        Show();
    }

    private string Cut(string body)
    {
        string flat = body.Replace("\n", " ").Replace("\r", " ");

        return flat.Length <= 200 ? flat : flat.Substring(0, 200) + "...";
    }

    private void Show()
    {
        if (output == null) return;

        // Пока ждём своё - так и пишем. Молчащая доска неотличима от сломанной.
        string tail = result == null ? steps + " steps - press to send" : "ждём ответ...";

        output.text = (log == "" ? "" : log + "\n\n") + tail;
    }
}
