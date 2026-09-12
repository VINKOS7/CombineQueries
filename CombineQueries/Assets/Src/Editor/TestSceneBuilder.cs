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

        // Существующий риг НЕ сносим. Расставленное руками - доски под углом, высота кнопок -
        // живёт только в сцене, и пересборка ради одной новой панели стирала бы всю работу.
        // Поэтому находим по имени и трогаем лишь то, чего нет.
        //
        // Числа ниже - раскладка, расставленная руками. Они нужны только НОВОЙ сцене: в существующей
        // всё берётся как есть.
        //
        // Ищем среди корней сцены, а не через GameObject.Find: тот не видит выключенные объекты, и
        // риг, скрытый галочкой «Dev rig visible», нашёлся бы как отсутствующий - билдер завёл бы
        // второй с тем же именем, и половина ссылок ушла бы в него.
        var root = Existing();

        if (root == null)
        {
            root = new GameObject(RigName);
            root.transform.position = new Vector3(0f, 0.702f, -0.5f);
        }

        // Цвета сняты из сцены один в один - те, в которые кубы перекрашены руками. Билдер ставит
        // их только НОВЫМ кубам: существующие он не перекрашивает, покраска такая же ручная работа,
        // как и расстановка досок. Значения не округлять: округлённые дают похожий, но другой цвет.
        var initBtn = MakeButton(root.transform, "Btn_Init", new Vector3(-0.6f, 0.34f, 0f), Color.black);
        var sendBtn = MakeButton(root.transform, "Btn_Send", new Vector3(0.6f, 0.363f, 0f), new Color(0f, 1f, 0.7669797f));

        // Третья кнопка - счётчик шагов. Он и есть релизный показ, но проверять его надо здесь же,
        // в дев-сцене: гонять ради одной кнопки другой мод незачем.
        var stepsBtn = MakeButton(root.transform, "Btn_Steps", new Vector3(2.81f, 0.35f, -1.25f), new Color(1f, 0.015686274f, 0.2557574f));

        // Доски: сводка прогона, ушедшее, пришедшее, тела и шаги. Числа сняты из сцены после того,
        // как их расставили руками; первые две координаты это anchored, третья - глубина, следом
        // поворот.
        var text = MakeBoard(root.transform, "StatusCanvas", new Vector3(0f, 3.23f, -0.095f), new Vector3(-326.507f, -180f, 0f));

        // ДВА ОТДЕЛЬНЫХ канваса по бокам: слева что ушло (vrequest), справа что вернулось (response
        // и vresponse). Вперемешку это не читается - ответы отстают от запросов на шаг-другой.
        var requestsText = MakeBoard(root.transform, "RequestsCanvas", new Vector3(-0.034f, 0.93f, 0.455f), new Vector3(0f, 361.678f, 0f));
        var responsesText = MakeBoard(root.transform, "ResponsesCanvas", new Vector3(-2.43f, 0.937f, -0.59f), new Vector3(0f, -43.947f, 0f));

        // Четвёртая доска - сами тела: чей набор, адрес и начало ответа.
        var dataText = MakeBoard(root.transform, "DataCanvas", new Vector3(0f, 2.137f, 0.421f), new Vector3(-174.577f, -180f, 0f));

        // Пятая - своя доска шагам: она переписывается на каждом шаге игрока и затирала бы сводку.
        var stepsText = MakeBoard(root.transform, "StepsCanvas", new Vector3(2.43f, 0.9f, -0.6f), new Vector3(0f, 44.865f, 0f));

        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log("[TestSceneBuilder] geometry created: cubes + canvas. Udon components next.");

        EnsureProgramAsset("Assets/Src/CombineQueries.cs");
        EnsureProgramAsset("Assets/Src/CombineQueriesTest.cs");
        EnsureProgramAsset("Assets/Src/SampleSteps.cs");

        try
        {
            // Компоненты тоже переиспользуем: второй AddUdonSharpComponent на той же кнопке завёл
            // бы двойника, и Udon стал бы слать событие дважды.
            var clientGo = root.transform.Find("CombineQueries")?.gameObject;

            if (clientGo == null)
            {
                clientGo = new GameObject("CombineQueries");
                clientGo.transform.SetParent(root.transform, false);
            }

            var client = clientGo.GetComponent<CombineQueries>() ?? clientGo.AddUdonSharpComponent<CombineQueries>();

            var initTest = initBtn.GetComponent<CombineQueriesTest>() ?? initBtn.AddUdonSharpComponent<CombineQueriesTest>();
            initTest.client = client;
            initTest.action = 0;
            initTest.codeword = CombineQueriesEnvironment.Codeword;
            initTest.output = text;

            var sendTest = sendBtn.GetComponent<CombineQueriesTest>() ?? sendBtn.AddUdonSharpComponent<CombineQueriesTest>();
            sendTest.client = client;
            sendTest.action = 1;
            sendTest.output = text;
            sendTest.requests = requestsText;
            sendTest.responses = responsesText;
            sendTest.data = dataText;

            var stepsRig = stepsBtn.GetComponent<SampleSteps>() ?? stepsBtn.AddUdonSharpComponent<SampleSteps>();
            stepsRig.client = client;
            stepsRig.output = stepsText;

            // Событие о завершении уходит ОДНОЙ цели - стенду. Риг шагов его не ждёт: он опрашивает
            // свою коробку сам, как это будет делать любой мир со стороны.
            client.target = sendTest;
            client.onDoneEvent = "OnQueryDone";

            UdonSharpEditorUtility.CopyProxyToUdon(client);
            UdonSharpEditorUtility.CopyProxyToUdon(initTest);
            UdonSharpEditorUtility.CopyProxyToUdon(sendTest);
            UdonSharpEditorUtility.CopyProxyToUdon(stepsRig);

            // Сохраняем сами: раньше сцена только помечалась грязной, и на Play уезжал старый риг,
            // если Ctrl+S забыли. Пересборка рига и так необратима - терять её на этом глупо.
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

            Debug.Log("[TestSceneBuilder] done and saved. Hit Play.");
        }
        catch (Exception e)
        {
            Debug.LogError("[TestSceneBuilder] Udon components were not attached: " + e.Message
                         + "\nGeometry is already in the scene. Wait for U# to finish compiling and run the menu item again.");
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    // Заводит program asset для скрипта, если его ещё НЕТ НИГДЕ в проекте.
    //
    // Искать надо по всему проекту, а не рядом со скриптом: U# требует ровно один program asset на
    // скрипт, а ассет остаётся на месте, когда скрипт переезжает в другую папку (ссылка держится
    // за GUID). Проверка «лежит ли рядом» на этом и погорела - завела второй и сломала компиляцию.
    internal static void EnsureProgramAsset(string scriptPath)
    {
        var script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);

        if (script == null) { Debug.LogError("[TestSceneBuilder] script not found: " + scriptPath); return; }

        foreach (string guid in AssetDatabase.FindAssets("t:UdonSharpProgramAsset"))
        {
            string known = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharp.UdonSharpProgramAsset>(known);

            if (asset != null && asset.sourceCsScript == script) return;
        }

        string assetPath = scriptPath.Substring(0, scriptPath.Length - 3) + ".asset";

        var programAsset = ScriptableObject.CreateInstance<UdonSharp.UdonSharpProgramAsset>();
        programAsset.sourceCsScript = script;

        AssetDatabase.CreateAsset(programAsset, assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        UdonSharp.UdonSharpProgramAsset.CompileAllCsPrograms(true);

        Debug.Log("[TestSceneBuilder] created program asset: " + assetPath);
    }

    // Отдельная доска: свой канвас со своим текстом. Существующую возвращаем как есть - её могли
    // повернуть и подвинуть руками, и это единственное место, где такая правка живёт.
    //
    // place: x и y - anchored, z - глубина. euler - поворот доски.
    private static Text MakeBoard(Transform parent, string name, Vector3 place, Vector3 euler)
    {
        var found = parent.Find(name);

        if (found != null)
        {
            var had = found.GetComponentInChildren<Text>(true);

            if (had != null) return had;

            UnityEngine.Object.DestroyImmediate(found.gameObject);
        }

        var canvasGo = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(parent, false);

        canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

        var canvasRt = canvasGo.GetComponent<RectTransform>();
        canvasRt.sizeDelta = DevRig.BoardSize;
        canvasRt.anchoredPosition = new Vector2(place.x, place.y);
        canvasRt.localPosition = new Vector3(canvasRt.localPosition.x, canvasRt.localPosition.y, place.z);
        canvasRt.localScale = Vector3.one * DevRig.BoardScale;
        canvasRt.localRotation = Quaternion.Euler(euler);

        var go = new GameObject(name + "Text", typeof(Text));
        go.transform.SetParent(canvasGo.transform, false);

        var text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 20;
        text.color = Color.white;
        text.alignment = TextAnchor.UpperLeft;
        text.text = "";

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(16, 16);
        rt.offsetMax = new Vector2(-16, -16);

        return text;
    }

    // Риг в сцене, даже если он выключен галочкой «Dev rig visible». GameObject.Find выключенных
    // не видит, и на нём билдер заводил второй риг с тем же именем.
    private static GameObject Existing()
    {
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            if (root.name == RigName) return root;

        return null;
    }

    // Кнопка: существующую возвращаем КАК ЕСТЬ - и позиция, и цвет у неё ручные. Аргументы идут
    // только новой.
    private static GameObject MakeButton(Transform parent, string name, Vector3 localPos, Color color)
    {
        var found = parent.Find(name);

        if (found != null) return found.gameObject;

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = Vector3.one * 0.3f;

        Paint(go, color);

        return go;
    }

    private static void Paint(GameObject go, Color color)
    {
        var renderer = go.GetComponent<Renderer>();

        if (renderer == null) return;

        // Свой материал каждому: общий встроенный красить нельзя - перекрасятся все кубы сцены,
        // включая чужие.
        if (renderer.sharedMaterial == null || renderer.sharedMaterial.color != color)
            renderer.sharedMaterial = new Material(Shader.Find("Standard")) { color = color };
    }
}
