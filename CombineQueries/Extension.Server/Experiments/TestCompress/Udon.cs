//using UdonSharp;
//using UnityEngine;
//using VRC.SDK3.Data;
//using VRC.SDK3.StringLoading;
//using VRC.SDKBase;

//// Идея: alphabetLevels НЕ строятся заново на каждое сообщение, а НАКАПЛИВАЮТСЯ между запросами.
//// Первый раз данные уходят "холодными" (много запросов), сервер в ответе присылает расширенные
//// уровни алфавита, клиент кладёт их в DataList. Второй раз те же/похожие данные жмутся сильнее
//// на основе уже известного расширенного алфавита - в идеале в 1 запрос.
//public class QueryRune : UdonSharpBehaviour
//{
//    [SerializeField] public string baseUrl = "http://localhost:5017";
//    [SerializeField] public string baseForwardUrl = "vink0s.com";
//    [SerializeField] public string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789-._~:/?#[]@!$&'()*+,;=%";
//    [SerializeField] public int RuneSize = 4;

//    // пул VRCUrl - ВСЁ ЕЩЁ проблема: строить VRCUrl можно только в field-init/компайл-тайме,
//    // GetRuneQueries в рантайме через циклы может не пройти в честном Udon. Оставлено как было.
//    private VRCUrl[] RuneQueries;
//    private VRCUrl InitQuery;

//    // КЛЮЧ идеи: накопленный словарь уровней алфавита, живёт между сообщениями
//    private DataList alphabetLevels = new DataList();

//    public string InitInfo = "";
//    public string CombineQueryResult = "";

//    void Start()
//    {
//        RuneQueries = GetRuneQueries(Alphabet, baseUrl);
//        InitQuery = new VRCUrl(baseUrl + "/init/?alphabet=" + Alphabet + "&baseForwardUrl=" + baseForwardUrl);

//        // уровень 0 - всегда базовый алфавит
//        alphabetLevels.Add(Alphabet);
//    }

//    public override void OnStringLoadSuccess(IVRCStringDownload response)
//    {
//        if (response.Url == InitQuery) { InitInfo = response.Result; return; }

//        CombineQueryResult = response.Result;

//        // сервер в ответе присылает НОВЫЕ уровни алфавита (по одному на строку) - копим их
//        AbsorbLevelsFromResponse(response.Result);
//    }

//    public override void OnStringLoadError(IVRCStringDownload result)
//    {
//        Debug.Log("Error: " + result.ErrorCode);
//        Debug.Log(result.Error);
//    }

//    public void Init() => VRCStringDownloader.LoadUrl(InitQuery);

//    public void Send(string url)
//    {
//        int[] runes = CompressRecursiveAccum(url, RuneSize);

//        VRCUrl query = QueryByRunesFrom(IntArrayToString(runes));

//        if (query != null) VRCStringDownloader.LoadUrl(query, this);
//    }

//    // Сжимает, ПЕРЕИСПОЛЬЗУЯ уже накопленные alphabetLevels (не с нуля). Новые уровни, которые
//    // появились в процессе, дописываются в общий DataList - их же вернёт сервер и подтвердит.
//    public int[] CompressRecursiveAccum(string input, int runeSize)
//    {
//        // старт с самого "прогретого" уровня, который у нас есть
//        string currentAlphabet = alphabetLevels[alphabetLevels.Count - 1].String;

//        int[] current = Compress(input, currentAlphabet);

//        while (current.Length > runeSize && current.Length % 2 == 0 && current.Length > 0)
//        {
//            string asString = IntArrayToString(current);

//            currentAlphabet = currentAlphabet + asString;
//            alphabetLevels.Add(currentAlphabet);

//            current = Compress(asString, currentAlphabet);
//        }

//        return current;
//    }

//    public int[] Compress(string input, string clientAlphabet)
//    {
//        if (string.IsNullOrEmpty(input) || input.Length % 2 != 0 || string.IsNullOrEmpty(clientAlphabet))
//            return new int[0];

//        int totalBlocks = input.Length / 2;
//        int[] res = new int[totalBlocks];

//        for (int b = 0; b < totalBlocks; b++)
//        {
//            int idx1 = clientAlphabet.IndexOf(input[b * 2]);
//            int idx2 = clientAlphabet.IndexOf(input[b * 2 + 1]);

//            if (idx1 < 0 || idx2 < 0) return new int[0];

//            res[b] = idx1 * clientAlphabet.Length + idx2;
//        }

//        return res;
//    }

//    // сервер прислал уровни (одна строка = один уровень), докладываем те, которых ещё нет
//    private void AbsorbLevelsFromResponse(string response)
//    {
//        if (string.IsNullOrEmpty(response)) return;

//        if (!VRCJson.TryDeserializeFromJson(response, out DataToken parsed)) return;
//        if (parsed.TokenType != TokenType.DataList) return;

//        DataList incoming = parsed.DataList;

//        for (int i = 0; i < incoming.Count; i++)
//        {
//            string level = incoming[i].String;

//            // держим в порядке: индекс i соответствует уровню i
//            if (i >= alphabetLevels.Count) alphabetLevels.Add(level);
//        }
//    }

//    private VRCUrl QueryByRunesFrom(string runes)
//    {
//        foreach (var q in RuneQueries) if (q.Get() == runes) return q;

//        return null;
//    }

//    private static string IntArrayToString(int[] array)
//    {
//        if (array == null || array.Length == 0) return "";

//        char[] res = new char[array.Length];
//        for (int i = 0; i < array.Length; i++) res[i] = (char)array[i];

//        return new string(res);
//    }

//    private static VRCUrl[] GetRuneQueries(string alph, string baseUri)
//    {
//        int len = alph.Length, idx = 0;
//        VRCUrl[] res = new VRCUrl[len * len];

//        for (int i = 0; i < len; i++)
//            for (int j = 0; j < len; j++)
//                res[idx++] = new VRCUrl(baseUri + alph[i] + alph[j]);

//        return res;
//    }
//}
