using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UdonSharpEditor;

// Риг для релиза: кнопка подключения, кнопка шагов и одна доска. Ни сводок, ни панелей запросов -
// в мире это лишнее, там смотрят не на механику, а на то, что адрес собрался и ответ пришёл.
public static class ReleaseRigBuilder
{
    private const string RigName = "CombineQueriesReleaseRig";

    [MenuItem("Tools/CombineQueries/Add release rig to current scene")]
    private static void Build()
    {
        var existing = GameObject.Find(RigName);

        if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

        var root = new GameObject(RigName);
        root.transform.position = new Vector3(0f, 0f, -0.5f);

        var connectBtn = MakeButton(root.transform, "Btn_Connect", new Vector3(-0.6f, 1f, 0f), Color.cyan);
        var stepsBtn = MakeButton(root.transform, "Btn_Steps", new Vector3(0.6f, 1f, 0f), Color.green);

        var text = MakeBoard(root.transform, "ReleaseCanvas", new Vector3(0f, 2f, 0.2f));

        Selection.activeGameObject = root;

        TestSceneBuilder.EnsureProgramAsset("Assets/Src/CombineQueries.cs");
        TestSceneBuilder.EnsureProgramAsset("Assets/Src/CombineQueriesTest.cs");
        TestSceneBuilder.EnsureProgramAsset("Assets/Src/SampleSteps.cs");

        try
        {
            var clientGo = new GameObject("CombineQueries");
            clientGo.transform.SetParent(root.transform, false);

            var client = clientGo.AddUdonSharpComponent<CombineQueries>();

            // Подключение осталось за тестовой кнопкой: она умеет и connect, и кодовое слово.
            var connect = connectBtn.AddUdonSharpComponent<CombineQueriesTest>();
            connect.client = client;
            connect.action = 0;
            connect.codeword = CombineQueriesEnvironment.Codeword;
            connect.output = text;

            var steps = stepsBtn.AddUdonSharpComponent<SampleSteps>();
            steps.client = client;
            steps.output = text;

            // Цель - кнопка подключения: OnQueryDone есть только у неё, и по нему доска говорит,
            // что подключились. Риг шагов события не ждёт - он опрашивает свою коробку сам.
            client.target = connect;
            client.onDoneEvent = "OnQueryDone";

            UdonSharpEditorUtility.CopyProxyToUdon(client);
            UdonSharpEditorUtility.CopyProxyToUdon(connect);
            UdonSharpEditorUtility.CopyProxyToUdon(steps);

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

            Debug.Log("[ReleaseRigBuilder] done and saved. Blue - connect, green - send your steps.");
        }
        catch (Exception e)
        {
            Debug.LogError("[ReleaseRigBuilder] Udon components were not attached: " + e.Message
                         + "\nGeometry is already in the scene. Wait for U# to finish compiling and run the menu item again.");
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    private static Text MakeBoard(Transform parent, string name, Vector3 localPos)
    {
        var canvasGo = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(parent, false);

        canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

        var canvasRt = canvasGo.GetComponent<RectTransform>();
        canvasRt.sizeDelta = DevRig.BoardSize;
        canvasRt.localPosition = localPos;
        canvasRt.localScale = Vector3.one * DevRig.BoardScale;
        canvasRt.localRotation = Quaternion.Euler(0f, 180f, 0f);

        var textGo = new GameObject(name + "Text", typeof(Text));
        textGo.transform.SetParent(canvasGo.transform, false);

        var text = textGo.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 28;
        text.color = Color.white;
        text.alignment = TextAnchor.UpperLeft;
        text.text = "press the blue cube to connect";

        var rt = textGo.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(16, 16);
        rt.offsetMax = new Vector2(-16, -16);

        return text;
    }

    private static GameObject MakeButton(Transform parent, string name, Vector3 localPos, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = Vector3.one * 0.3f;

        var mat = new Material(Shader.Find("Standard"));
        mat.color = color;
        go.GetComponent<Renderer>().sharedMaterial = mat;

        return go;
    }
}
