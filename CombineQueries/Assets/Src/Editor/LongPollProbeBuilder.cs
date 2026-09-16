using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UdonSharpEditor;

// Ставит в сцену куб со щупом конкурентности и заводит его program asset. Отдельно от
// TestSceneBuilder: щуп диагностический, в продукт не входит и в Udon/ не синхронизируется.
public static class LongPollProbeBuilder
{
    [MenuItem("Tools/CombineQueries/Add long-poll probe")]
    private static void Build()
    {
        TestSceneBuilder.EnsureProgramAsset("Assets/Src/LongPollProbe.cs");

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "LongPollProbe";
        go.transform.position = new Vector3(0f, 1f, 2f);

        try
        {
            var probe = go.GetComponent<LongPollProbe>() ?? go.AddUdonSharpComponent<LongPollProbe>();

            UdonSharpEditorUtility.CopyProxyToUdon(probe);

            Debug.Log("[LongPollProbe] куб добавлен. Включи Allow untrusted URLs, жми Play, смотри консоль.");
        }
        catch (System.Exception e)
        {
            Debug.LogError("[LongPollProbe] компонент не привязался: " + e.Message
                         + "\nКуб уже в сцене. Дождись компиляции U# и запусти пункт меню ещё раз.");
        }

        Selection.activeGameObject = go;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }
}
