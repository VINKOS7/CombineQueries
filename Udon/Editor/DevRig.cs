using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Дев-выводы (доска с числами запросов и кнопки прогона) живут в ОТДЕЛЬНОМ РИГЕ, а не в отдельной
// сцене: две сцены пришлось бы править парой, и они разъехались бы на первой же правке клиента.
//
// Галочка Dev mode переключает РОВНО ОДНО - персист хайперов, единственное, чем релиз сегодня
// отличается от дева. Видимости рига она не касается: спрятать его можно его же галочкой в
// иерархии, а завязывать на один пин две несвязанные вещи мы уже пробовали.
//
// Отдельного билдера релизного рига больше нет: он собирал второй набор объектов с нуля, каждый раз
// снося расставленное руками, а показывал то же самое. Лежит в Udon/Archive, если понадобится.
public static class DevRig
{
    private const string RigName = "CombineQueriesRig";

    // Дефайн полного персиста. Стоит - хайперы копятся в БД, как на релизе; снят - живут в ОЗУ
    // сервера, а в базе остаётся один посев из миграции. Читает его CombineQueries.dev.cs.
    private const string Define = "CQ_PERSIST";

    private const string Item = "Tools/CombineQueries/Dev mode";

    [MenuItem(Item)]
    private static void Toggle()
    {
        bool dev = IsDev();

        var defines = new List<string>(Defines);

        // Dev mode включаем - персист снимаем, и наоборот. Галочка называет режим, а не дефайн.
        if (dev) defines.Add(Define); else defines.Remove(Define);

        PlayerSettings.SetScriptingDefineSymbols(Target, defines.ToArray());

        Debug.Log("[DevRig] dev mode " + (dev ? "снят: hypers=on, полный персист как на релизе" : "включён: hypers=off, хайперы живут в ОЗУ сервера")
                + ". U# перекомпилируется, пул ссылок перепечётся на следующем входе в мир - после этого нажми Connect.");
    }

    [MenuItem(Item, true)]
    private static bool Mark()
    {
        Menu.SetChecked(Item, IsDev());

        return true;
    }

    // Dev mode - это ОТСУТСТВИЕ персиста: пока дефайна нет, сервер держит хайперы в памяти и
    // забывает их на перезапуске, что и нужно, чтобы мерить сборку с чистого листа.
    private static bool IsDev() => Array.IndexOf(Defines, Define) < 0;

    private static NamedBuildTarget Target =>
        NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));

    private static string[] Defines
    {
        get
        {
            PlayerSettings.GetScriptingDefineSymbols(Target, out string[] defines);

            return defines;
        }
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
