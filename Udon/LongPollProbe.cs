using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;

// ЩУП конкурентности VRCStringDownloader. Отвечает на ОДИН вопрос, от которого зависит вся идея с
// нитро: можно ли держать несколько загрузок в полёте одновременно, или SDK сериализует их и
// стартует следующую лишь после возврата предыдущей.
//
// Бьём тремя загрузками подряд, без пауз, по РАЗНЫМ url на эндпоинт, который держит ответ 10 с
// (httpbin.org/delay/10). Абсолютное время от старта НЕ показатель: если очередь загрузчика занята
// другими (клиент, риги), загрузки простоят в ней, и время раздуется. Показатель - РАЗНИЦА между
// возвратами, она от простоя в очереди не зависит:
//
//   Δ между возвратами ~5 с  - старты не ждут завершения. Пул лонгпулов с одного клиента реален,
//                              конденсатор заряжается, нитро работает.
//   Δ между возвратами ~10 с - SDK сериализует. Больше одной загрузки в полёте не бывает, нитро
//                              возможно только пулом по игрокам.
//
// ВАЖНО: гонять в ПУСТОЙ сцене, где только этот куб. Иначе клиент и риги забьют общую очередь
// загрузчика, и первый возврат уедет на десятки секунд (это и был `+22000`).
//
// В VRChat включить «Allow untrusted URLs»: httpbin - недоверенный домен, иначе строки придут как
// err. Если первый возврат раньше ~10 с - сервер НЕ додержал ответ (httpbin режет), замер
// недействителен, смени эндпоинт (напр. https://postman-echo.com/delay/10).
//
// Диагностика, в продукт не входит, в Udon/ не синхронизируется.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class LongPollProbe : UdonSharpBehaviour
{
    [Tooltip("Куда писать результат (необязательно, дублируется в консоль)")]
    public Text output;

    // Три РАЗНЫХ url: один эндпоинт, разный query - чтобы SDK не схлопнул их как одинаковые.
    private readonly VRCUrl[] urls = new VRCUrl[]
    {
        new VRCUrl("https://httpbin.org/delay/10?probe=1"),
        new VRCUrl("https://httpbin.org/delay/10?probe=2"),
        new VRCUrl("https://httpbin.org/delay/10?probe=3"),
    };

    private bool running;
    private float firedAt;
    private float lastAt;
    private int back;

    // Время первого возврата от старта и дельты второго и третьего от предыдущего.
    private int first;
    private int delta2;
    private int delta3;

    private string log = "";

    // На старте сцены - чтобы работал без рук. Повторный запуск - по нажатию на куб.
    private void Start() => Fire();

    public override void Interact() => Fire();

    private void Fire()
    {
        // Пока идут три ответа, второе нажатие игнорируем: иначе в очередь уйдёт ещё три и всё
        // смешается.
        if (running) { Debug.Log("[LongPollProbe] уже идёт, жду три ответа"); return; }

        running = true;
        firedAt = Time.time;
        lastAt = firedAt;
        back = 0;

        log = "щуп: три загрузки в t=0, ждём (сервер держит каждую 10 с)...";

        Show();

        // Три подряд, без пауз между вызовами: именно это и проверяем.
        for (int i = 0; i < urls.Length; i++) VRCStringDownloader.LoadUrl(urls[i], this);
    }

    public override void OnStringLoadSuccess(IVRCStringDownload result) => Note("ok");

    public override void OnStringLoadError(IVRCStringDownload result) => Note("err " + result.ErrorCode);

    private void Note(string how)
    {
        back++;

        int fromFire = (int)((Time.time - firedAt) * 1000f);
        int fromPrev = (int)((Time.time - lastAt) * 1000f);

        lastAt = Time.time;

        if (back == 1) first = fromFire;
        if (back == 2) delta2 = fromPrev;
        if (back == 3) delta3 = fromPrev;

        log = log + "\n" + back + ": " + how + "   +" + fromFire + " мс от старта   Δ" + fromPrev + " мс от прошлого";

        Debug.Log("[LongPollProbe] " + back + ": " + how + " +" + fromFire + "мс Δ" + fromPrev + "мс");

        Show();

        if (back >= urls.Length) Verdict();
    }

    private void Verdict()
    {
        running = false;

        string v;

        // Первый возврат раньше ~9 с - сервер не додержал 10 с, мерить нечего.
        if (first < 9000)
            v = "ЭНДПОИНТ НЕ ДЕРЖИТ (httpbin режет или ошибка) - смени эндпоинт, замер недействителен";
        else
        {
            int avg = (delta2 + delta3) / 2;

            v = avg < 7500 ? "ПУЛ РАБОТАЕТ: старты не ждут завершения, конденсатор реален, нитро с клиента есть"
              : avg < 12500 ? "СЕРИАЛИЗАЦИЯ: одна загрузка в полёте, нитро только пулом по игрокам"
              : "НЕЯСНО: повтори в ПУСТОЙ сцене, только этот куб";
        }

        log = log + "\n=== " + v + " ===   Δ2=" + delta2 + " Δ3=" + delta3;

        Debug.Log("[LongPollProbe] " + v + " (Δ2=" + delta2 + " Δ3=" + delta3 + ")");

        Show();
    }

    private void Show()
    {
        if (output != null) output.text = log;
    }
}
