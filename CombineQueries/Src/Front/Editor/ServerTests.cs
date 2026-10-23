using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// Проводные тесты сервера (Src/Back/VQueries.Tests) прямо из редактора: кнопка запускает
// `dotnet test` отдельным процессом, вывод идёт в окно строка за строкой. Редактор не замерзает -
// процесс живёт сам, а окно забирает накопленное на каждом тике.
//
// Сервер должен быть запущен (CQ_HOST, по умолчанию http://localhost:5017), и у него должны быть
// свободные потоки: прогон берёт около шести из восьми.
public class ServerTests : EditorWindow
{
    // Сколько строк держать в окне: на тысячах GUILayout.Label окно начинает тормозить.
    private const int MaxLines = 4000;

    private readonly ConcurrentQueue<string> _incoming = new ConcurrentQueue<string>();
    private readonly List<string> _lines = new List<string>();
    private readonly List<string> _results = new List<string>();

    private Process _process;
    private Vector2 _scroll;
    private bool _follow = true;
    private string _filter = "";
    private string _summary = "";
    private DateTime _started;

    [MenuItem("Tools/CombineQueries/Server tests")]
    private static void Open() => GetWindow<ServerTests>("Server tests");

    private static string TestsPath => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Src", "Back", "VQueries.Tests");

    private bool Running => _process != null && !_process.HasExited;

    private void OnEnable() => EditorApplication.update += Pump;

    private void OnDisable()
    {
        EditorApplication.update -= Pump;

        Stop();
    }

    private void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(Running))
            {
                if (GUILayout.Button("Run", GUILayout.Width(80))) Run();
            }

            using (new EditorGUI.DisabledScope(!Running))
            {
                if (GUILayout.Button("Stop", GUILayout.Width(80))) Stop();
            }

            GUILayout.Label("Filter", GUILayout.Width(40));
            _filter = EditorGUILayout.TextField(_filter);

            _follow = GUILayout.Toggle(_follow, "Follow", GUILayout.Width(60));
        }

        EditorGUILayout.LabelField("Tests", TestsPath, EditorStyles.miniLabel);

        if (Running) EditorGUILayout.LabelField($"Running... {(DateTime.Now - _started).TotalSeconds:0} s", EditorStyles.boldLabel);
        else if (_summary != "") EditorGUILayout.LabelField(_summary, EditorStyles.boldLabel);

        // Итог по тестам - отдельным списком над логом: пролистывать ради него тысячи строк незачем.
        foreach (string result in _results) EditorGUILayout.LabelField(result, Rich);

        EditorGUILayout.Space();

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        foreach (string line in _lines) EditorGUILayout.SelectableLabel(line, Mono, GUILayout.Height(14));

        EditorGUILayout.EndScrollView();
    }

    private void Run()
    {
        _lines.Clear();
        _results.Clear();
        _summary = "";
        _started = DateTime.Now;

        if (!Directory.Exists(TestsPath)) { _summary = "Not found: " + TestsPath; return; }

        // Детальный логгер консоли: только он печатает Passed/Failed каждого теста и вывод
        // TestContext.Out - в том числе таблицу HyperTickTests.
        string arguments = "test --logger \"console;verbosity=detailed\"";

        if (_filter.Trim() != "") arguments += $" --filter \"FullyQualifiedName~{_filter.Trim()}\"";

        var start = new ProcessStartInfo(Dotnet(), arguments)
        {
            WorkingDirectory = TestsPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        start.EnvironmentVariables["DOTNET_CLI_UI_LANGUAGE"] = "en";
        start.EnvironmentVariables["DOTNET_NOLOGO"] = "1";

        _process = new Process { StartInfo = start, EnableRaisingEvents = true };

        _process.OutputDataReceived += (_, line) => { if (line.Data != null) _incoming.Enqueue(line.Data); };
        _process.ErrorDataReceived += (_, line) => { if (line.Data != null) _incoming.Enqueue("[err] " + line.Data); };

        try
        {
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            _incoming.Enqueue($"> dotnet {arguments}");
        }
        catch (Exception error)
        {
            _summary = "dotnet did not start: " + error.Message;
            _process = null;
        }
    }

    private void Stop()
    {
        if (!Running) return;

        // Дерево целиком: dotnet test держит дочерний testhost, и Kill() снял бы только родителя.
        // Kill(true) Unity не видит - его профиль .NET Standard 2.1.
        try
        {
            Process.Start(new ProcessStartInfo("taskkill", $"/PID {_process.Id} /T /F") { UseShellExecute = false, CreateNoWindow = true })?.WaitForExit(5000);
        }
        catch (Exception)
        {
            // Уже вышел сам - добивать нечего.
        }

        _summary = "Stopped";
    }

    // Забирает накопленный вывод на главном потоке: GUI трогать из колбэков процесса нельзя.
    private void Pump()
    {
        bool changed = false;

        while (_incoming.TryDequeue(out string line))
        {
            _lines.Add(line);

            Classify(line);

            changed = true;
        }

        if (_lines.Count > MaxLines) _lines.RemoveRange(0, _lines.Count - MaxLines);

        if (_process != null && _process.HasExited && _incoming.IsEmpty && _summary == "")
        {
            _summary = (_process.ExitCode == 0 ? "PASSED" : "FAILED") + $" (exit {_process.ExitCode}, {(DateTime.Now - _started).TotalSeconds:0} s)";

            UnityEngine.Debug.Log("[ServerTests] " + _summary + "\n" + string.Join("\n", _results));

            changed = true;
        }

        if (!changed && !Running) return;

        if (_follow && changed) _scroll.y = float.MaxValue;

        Repaint();
    }

    private void Classify(string line)
    {
        string trimmed = line.TrimStart();

        if (trimmed.StartsWith("Passed ") && !trimmed.StartsWith("Passed!")) _results.Add("<color=#3c3>✔</color> " + trimmed.Substring(7));
        else if (trimmed.StartsWith("Failed ") && !trimmed.StartsWith("Failed!")) _results.Add("<color=#e44>✘</color> " + trimmed.Substring(7));
        else if (trimmed.StartsWith("Skipped ")) _results.Add("<color=#cc3>–</color> " + trimmed.Substring(8));
        else if (trimmed.StartsWith("Passed!") || trimmed.StartsWith("Failed!")) _summary = "";
    }

    // dotnet из PATH; Unity, запущенный из Hub, PATH пользователя видит не всегда.
    private static string Dotnet()
    {
        string installed = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");

        return File.Exists(installed) ? installed : "dotnet";
    }

    private static GUIStyle _rich;
    private static GUIStyle Rich => _rich ??= new GUIStyle(EditorStyles.label) { richText = true };

    private static GUIStyle _mono;
    private static GUIStyle Mono => _mono ??= new GUIStyle(EditorStyles.label) { font = Font.CreateDynamicFontFromOSFont("Consolas", 11), fontSize = 11 };
}
