using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Дев-выводы (доска с числами запросов и кнопки прогона) живут в ОТДЕЛЬНОМ РИГЕ, а не в отдельной
// сцене: две сцены пришлось бы править парой, и они разъехались бы на первой же правке клиента.
//
// Кнопка прячет риг целиком - этого хватает, чтобы в релизном мире не было видно ни доски, ни
// кнопок. Числа, по которым читается «до и после персиста», в релизном моде и так не печатаются
// (см. CQ_RELEASE в CombineQueries и CombineQueriesTest) - риг лишь убирает саму сцену показа.
[InitializeOnLoad]
public static class DevRig
{
    private const string RigName = "CombineQueriesRig";

    // Сцена хранит риг таким, каким его сохранили: пересобрал через Tools, не нажал Ctrl+S - и при
    // следующем открытии снова узкая доска. Поэтому подгоняем её на открытии сцены сами.
    static DevRig() => EditorSceneManager.sceneOpened += (scene, mode) => FitBoard();
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
