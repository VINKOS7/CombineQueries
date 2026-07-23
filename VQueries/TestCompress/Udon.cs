
//using System.Diagnostics;
//using UdonSharp;
//using UnityEngine;
//using VRC.SDK3.Data;
//using VRC.SDK3.StringLoading;
//using VRC.SDKBase;

//public class QueryRune : UdonSharpBehaviour
//{
//    [SerializeField] public const string baseUrl = "http://localhost:5017";
//    [SerializeField] public const string baseForwardUrl = "vink0s.com";// not work now
//    [SerializeField] public const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789-._~:/?#[]@!$&'()*+,;=%";
//    [SerializeField] public const int RuneSize = 2;

//    private readonly VRCUrl[] RuneQueries = GetRuneQueries(Alphabet, baseUrl);
//    private readonly VRCUrl InitQuery = new VRCUrl($"{baseUrl}/init/?alphabet={Alphabet}&baseForwardUrl={baseForwardUrl}");

//    public string InitInfo = string.Empty;
//    public string CombineQueryResult = string.Empty;
//    public string WebContext = string.Empty;// in progress, not work


//    public override void OnStringLoadSuccess(IVRCStringDownload response)
//    {//faaaaaaaaaaaaaaaaaaaaaaaaack is facking GET REQUESTS, AAAAAAAAAAAAAAAAAAAAA, СУка ебанаты тупые блять, таким способом работать с гет запросами, меня еще, сукааааааа
//        if (response.Url == InitQuery) InitInfo = response.Result;

//        CombineQueryResult = response.Result;

//    }

//    public override void OnStringLoadError(IVRCStringDownload result)
//    {
//        Debug.Log("Error: " + result.ErrorCode);

//        Debug.Log(result.Error);
//    }

//    public void Init() => VRCStringDownloader.LoadUrl(InitQuery);

//    public void Send(string url) => VRCStringDownloader.LoadUrl(QueryByRunesFrom(IntArrayToString(CompressRecursive(url, Alphabet, RuneSize, out DataList alphabetLevels))));

//    public static int[] Compress(string input, string clientAlphabet)
//    {
//        if (string.IsNullOrEmpty(input) || input.Length % 2 != 0 || string.IsNullOrEmpty(clientAlphabet)) return new int[0];

//        int totalBlocks = input.Length / 2;
//        int[] res = new int[totalBlocks];

//        for (int b = 0; b < totalBlocks; b++)
//        {
//            int idx1 = clientAlphabet.IndexOf(input[b * 2]);
//            int idx2 = clientAlphabet.IndexOf(input[b * 2 + 1]);

//            if (idx1 < 0 || idx2 < 0) return new int[0];

//            // Формула плоской матрицы: переводим пару в один уникальный ID
//            res[b] = (idx1 * clientAlphabet.Length) + idx2;
//        }

//        return res;
//    }

//    public int[] CompressRecursive(string input, string alphabet, int RuneSize, out DataList delta)
//    {
//        delta = new DataList(alphabet);

//        string currentAlphabet = alphabet;
//        int[] current = Compress(input, currentAlphabet);

//        while (current.Length > RuneSize && current.Length % 2 == 0 && current.Length > 0)
//        {
//            string asString = IntArrayToString(current);

//            currentAlphabet = currentAlphabet + asString;
//            delta.Add(currentAlphabet);

//            current = Compress(asString, currentAlphabet);
//        }

//        return current;
//    }

//    private VRCUrl QueryByRunesFrom(string runes)
//    {
//        foreach (var tQuery in RuneQueries) if (tQuery.Get() == runes) return tQuery;

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
//        VRCUrl[] res = new VRCUrl[len * len * len * len];

//        for (int i = 0; i < len; i++)
//            for (int j = 0; j < len; j++)
//                res[idx++] = new VRCUrl(baseUri + alph[i] + alph[j]);
//        return res;
//    }
//}