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
// (httpbin.org/delay/10). Логируем, через сколько от первого старта вернулась каждая:
//
//   ~10, 15, 20 с (Δ5)  - старты не ждут завершения. Пул лонгпулов с одного клиента реален,
//                         конденсатор заряжается, нитро работает.
//   ~10, 20, 30 с (Δ10) - SDK сериализует. Больше одной загрузки в полёте не бывает, нитро
//                         возможно только пулом по игрокам.
//
// В VRChat включить «Allow untrusted URLs»: httpbin - недоверенный домен, иначе придёт ошибка.
// Ни протокол, ни сам клиент это не трогает - отдельная диагностическая сцена, в продукт не входит.
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

    private float firedAt;
    private int back;
    private string log = "";

    // На старте сцены - чтобы работал без рук. Повторный запуск - по нажатию на куб.
    private void Start() => Fire();

    public override void Interact() => Fire();

    private void Fire()
    {
        firedAt = Time.time;
        back = 0;
        log = "щуп: три загрузки в t=0, ждём...";

        Show();

        // Три подряд, без пауз между вызовами: именно это и проверяем.
        for (int i = 0; i < urls.Length; i++) VRCStringDownloader.LoadUrl(urls[i], this);
    }

    public override void OnStringLoadSuccess(IVRCStringDownload result) => Note("ok");

    public override void OnStringLoadError(IVRCStringDownload result) => Note("err " + result.ErrorCode);

    // Порядок возврата и время от первого старта. Что именно вернулось - неважно, важен паттерн
    // времён: Δ5 против Δ10.
    private void Note(string how)
    {
        back++;

        int ms = (int)((Time.time - firedAt) * 1000f);

        log = log + "\n" + back + ": " + how + "   +" + ms + " ms";

        Debug.Log("[LongPollProbe] " + back + ": " + how + " +" + ms + "ms");

        Show();
    }

    private void Show()
    {
        if (output != null) output.text = log;
    }
}
