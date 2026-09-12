using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Дев-выводы (доска с числами запросов и кнопки прогона) живут в ОТДЕЛЬНОМ РИГЕ, а не в отдельной
// сцене: две сцены пришлось бы править парой, и они разъехались бы на первой же правке клиента.
//
// Эта галочка и есть ЕДИНСТВЕННОЕ переключение дев/релиз в сцене. Отдельного билдера релизного
// рига больше нет: он собирал второй набор объектов с нуля, каждый раз снося расставленное руками,
// а показывал то же самое. Лежит в Udon/Archive, если понадобится.
//
// Кнопка прячет риг целиком - этого хватает, чтобы в релизном мире не было видно ни доски, ни
// кнопок. Числа, по которым читается «до и после персиста», в релизном моде и так не печатаются
// (см. CQ_PROD в CombineQueries и CombineQueriesTest) - риг лишь убирает саму сцену показа.
public static class DevRig
{
    private const string RigName = "CombineQueriesRig";

    // На открытии сцены НИЧЕГО не трогаем. Раньше здесь стояла автоподгонка доски, и она молча
    // переписывала размер первому попавшемуся канвасу - то есть стирала расстановку, сделанную
    // руками. Ширину теперь задаёт билдер при создании, а подогнать старую сцену можно пунктом
    // меню, осознанно.
    private const string Item = "Tools/CombineQueries/Dev rig visible";

    [MenuItem(Item)]
    private static void Toggle()
    {
        var rig = Rig();

        if (rig == null) return;

        Undo.RecordObject(rig, "Toggle dev rig");

        rig.SetActive(!rig.activeSelf);

        EditorSceneManager.MarkSceneDirty(rig.scene);

        Debug.Log("[DevRig] " + RigName + (rig.activeSelf ? " показан" : " скрыт"));
    }

    [MenuItem(Item, true)]
    private static bool Mark()
    {
        var rig = Rig();

        Menu.SetChecked(Item, rig != null && rig.activeSelf);

        return rig != null;
    }

    // Вид сцены после возни с досками уезжает так, что рига не видно вовсе. Возвращаем камеру на
    // него - как игрок при спавне, только чуть выше и дальше, чтобы все пять досок попали в кадр.
    [MenuItem("Tools/CombineQueries/Reset scene view")]
    private static void ResetView()
    {
        var view = SceneView.lastActiveSceneView;

        if (view == null) { Debug.LogWarning("[DevRig] нет открытого окна сцены"); return; }

        var rig = Rig();
        var at = rig == null ? new Vector3(0f, 1.5f, 0f) : rig.transform.position + new Vector3(0f, 1.3f, 0f);

        view.orthographic = false;

        // Смотрим со стороны спавна: игрок стоит по -Z, доски развёрнуты к нему.
        view.LookAt(at, Quaternion.Euler(10f, 0f, 0f), 5f);

        view.Repaint();
    }

    // Ширина доски. Билдер уже создаёт широкую, но сцена, собранная раньше, про это не знает -
    // а пересобирать риг ради размера значит заново расставлять ссылки. Правим на месте.
    private const string Fit = "Tools/CombineQueries/Fit board width";

    // Единственное место, где заданы размеры доски. Их же берёт TestSceneBuilder, когда собирает
    // риг с нуля - иначе новая сцена и подгонка старой разъезжаются при первой же правке.
    internal static readonly Vector2 BoardSize = new(1800, 760);
    internal const float BoardScale = 0.0016f;

    [MenuItem(Fit)]
    internal static void FitBoard()
    {
        var rig = Rig();

        if (rig == null) return;

        var canvas = rig.GetComponentInChildren<Canvas>(true);

        if (canvas == null) { Debug.LogWarning("[DevRig] в риге нет Canvas"); return; }

        var rt = canvas.GetComponent<RectTransform>();

        // Уже широкая - молчим: иначе каждое открытие сцены помечало бы её изменённой на пустом месте.
        if (rt.sizeDelta == BoardSize) return;

        Undo.RecordObject(rt, "Fit board width");

        rt.sizeDelta = BoardSize;
        rt.localScale = Vector3.one * BoardScale;

        EditorSceneManager.MarkSceneDirty(rig.scene);

        Debug.Log("[DevRig] доска расширена: 1800x760 - сохрани сцену (Ctrl+S), иначе вернётся старая");
    }

    [MenuItem(Fit, true)]
    private static bool FitMark() => Rig() != null;

    // Ищем по корням сцены, а не GameObject.Find: тот не видит выключенные объекты, а именно их
    // нам и надо находить, чтобы вернуть риг обратно.
    private static GameObject Rig()
    {
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            if (root.name == RigName) return root;

        return null;
    }
}
