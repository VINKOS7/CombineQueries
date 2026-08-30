using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UdonSharpEditor;

public static class TestSceneBuilder
{
    private const string RigName = "CombineQueriesRig";

    [InitializeOnLoadMethod]
    private static void Announce() => Debug.Log("[TestSceneBuilder] ready: Tools > CombineQueries > Add test rig to current scene");

    [MenuItem("GameObject/CombineQueries Test Rig", false, 10)]
    [MenuItem("Tools/CombineQueries/Add test rig to current scene")]
    private static void Build()
    {
        Debug.Log("[TestSceneBuilder] building the rig");

        var existing = GameObject.Find(RigName);

        if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

        var root = new GameObject(RigName);
        root.transform.position = new Vector3(0f, 0f, -0.5f);

        var initBtn = MakeButton(root.transform, "Btn_Init", new Vector3(-0.6f, 1f, 0f), Color.cyan);
        var sendBtn = MakeButton(root.transform, "Btn_Send", new Vector3(0.6f, 1f, 0f), Color.green);

        var canvasGo = new GameObject("StatusCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(root.transform, false);

        canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

        var canvasRt = canvasGo.GetComponent<RectTransform>();
        canvasRt.sizeDelta = new Vector2(900, 700);
        canvasRt.localPosition = new Vector3(0f, 2f, 0.2f);
        canvasRt.localScale = Vector3.one * 0.002f;
        canvasRt.localRotation = Quaternion.Euler(0f, 180f, 0f);

        var textGo = new GameObject("StatusText", typeof(Text));
        textGo.transform.SetParent(canvasGo.transform, false);

        var text = textGo.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 22;
        text.color = Color.white;
        text.alignment = TextAnchor.UpperLeft;
        text.text = "waiting...";

        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(16, 16);
        textRt.offsetMax = new Vector2(-16, -16);

        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log("[TestSceneBuilder] geometry created: cubes + canvas. Udon components next.");

        EnsureProgramAsset("Assets/CombineQueries/CombineQueries.cs");
        EnsureProgramAsset("Assets/CombineQueries/CombineQueriesTest.cs");

        try
        {
            var clientGo = new GameObject("CombineQueries");
            clientGo.transform.SetParent(root.transform, false);

            var client = clientGo.AddUdonSharpComponent<CombineQueries>();

            var initTest = initBtn.AddUdonSharpComponent<CombineQueriesTest>();
            initTest.client = client;
            initTest.action = 0;
            initTest.codeword = "grach";
            initTest.output = text;

            var sendTest = sendBtn.AddUdonSharpComponent<CombineQueriesTest>();
            sendTest.client = client;
            sendTest.action = 1;
            sendTest.output = text;

            client.target = sendTest;
            client.onDoneEvent = "OnQueryDone";

            UdonSharpEditorUtility.CopyProxyToUdon(client);
            UdonSharpEditorUtility.CopyProxyToUdon(initTest);
            UdonSharpEditorUtility.CopyProxyToUdon(sendTest);

            Debug.Log("[TestSceneBuilder] done. Save the scene (Ctrl+S) and hit Play.");
        }
        catch (Exception e)
        {
            Debug.LogError("[TestSceneBuilder] Udon components were not attached: " + e.Message
                         + "\nGeometry is already in the scene. Wait for U# to finish compiling and run the menu item again.");
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    private static void EnsureProgramAsset(string scriptPath)
    {
        string assetPath = scriptPath.Substring(0, scriptPath.Length - 3) + ".asset";

        if (AssetDatabase.LoadAssetAtPath<UdonSharp.UdonSharpProgramAsset>(assetPath) != null) return;

        var script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);

        if (script == null) { Debug.LogError("[TestSceneBuilder] script not found: " + scriptPath); return; }

        var programAsset = ScriptableObject.CreateInstance<UdonSharp.UdonSharpProgramAsset>();
        programAsset.sourceCsScript = script;

        AssetDatabase.CreateAsset(programAsset, assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        UdonSharp.UdonSharpProgramAsset.CompileAllCsPrograms(true);

        Debug.Log("[TestSceneBuilder] created program asset: " + assetPath);
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
