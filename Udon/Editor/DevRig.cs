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
public static class DevRig
{
    private const string RigName = "CombineQueriesRig";
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

    // Ищем по корням сцены, а не GameObject.Find: тот не видит выключенные объекты, а именно их
    // нам и надо находить, чтобы вернуть риг обратно.
    private static GameObject Rig()
    {
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            if (root.name == RigName) return root;

        return null;
    }
}
