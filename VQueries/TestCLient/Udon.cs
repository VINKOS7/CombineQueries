
//using System.Diagnostics;
//using UdonSharp;
//using UnityEngine;
//using VRC.SDK3.StringLoading;
//using VRC.SDKBase;

//public class QueryCombiner : UdonSharpBehaviour
//{
//    [SerializeField] public const string baseUrl = "http://localhost:5017";
//    [SerializeField] public const string baseForwardUrl = "vink0s.com";// not work now
//    [SerializeField] public const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789-._~:/?#[]@!$&'()*+,;=%";
//    [SerializeField] public const int SizeChein = 4;

//    private readonly VRCUrl[] RunesQueries = GetQueries(Alphabet, baseUrl);
//    private readonly VRCUrl InitQuery = new VRCUrl($"{baseUrl}/init/?alphabet={Alphabet}&baseForwardUrl={baseForwardUrl}");

//    public string InitInfo = string.Empty;
//    public string CombineQueryResult = string.Empty;

//    public override void OnStringLoadSuccess(IVRCStringDownload response)
//    {//faaaaaaaaaaaaaaaaaaaaaaaaack is facking GET REQUESTS, AAAAAAAAAAAAAAAAAAAAA, СУка ебанаты тупые блять, таким способом работать с гет запросами, меня еще, сукааааааа
//        if (response.Url == InitQuery) InitInfo = response.Result;

//        if (IsCombineQueryRequest(response.Url)) CombineQueryResult = response.Result;

//        Debug.Log(response.Result);
//    }
//    public override void OnStringLoadError(IVRCStringDownload result)
//    {
//        Debug.Log("Error: " + result.ErrorCode);

//        Debug.Log(result.Error);
//    }

//    public void Init() => VRCStringDownloader.LoadUrl(InitQuery);
//    public void Send(string url)
//    {
//        var tQueries = new VRCUrl[url.Length / SizeChein * 2];

//        int idx = 0;

//        foreach (var symbols in SplitStr(url, SizeChein * 2))
//        {
//            tQueries[idx] = QueryByRunesFrom(IntArrayToString(Compress(symbols, Alphabet)));

//            ++idx;
//        }

//        SendCombineQuery(tQueries);
//    }
//    public void SendCombineQuery(VRCUrl[] combines)
//    {
//        foreach (var query in combines) VRCStringDownloader.LoadUrl(query, this);
//    }
//    public static int[] Compress(string input, string clientAlphabet)
//    {
//        if (string.IsNullOrEmpty(input) || input.Length % 2 != 0 || string.IsNullOrEmpty(clientAlphabet)) return new int[0];

//        int baseLen = clientAlphabet.Length;
//        int totalBlocks = input.Length / 2;
//        int[] res = new int[totalBlocks];

//        for (int b = 0; b < totalBlocks; b++)
//        {
//            int idx1 = clientAlphabet.IndexOf(input[b * 2]);
//            int idx2 = clientAlphabet.IndexOf(input[b * 2 + 1]);
//            if (idx1 < 0 || idx2 < 0) return new int[0];

//            // Формула плоской матрицы: переводим пару в один уникальный ID
//            res[b] = (idx1 * baseLen) + idx2;
//        }
//        return res;
//    }
//    public static string Decompress(int[] input, string clientAlphabet)
//    {
//        if (input == null || input.Length == 0 || string.IsNullOrEmpty(clientAlphabet)) return "";

//        int baseLen = clientAlphabet.Length;
//        char[] res = new char[input.Length * 2];
//        int resIdx = 0;

//        for (int b = 0; b < input.Length; b++)
//        {
//            int id = input[b];

//            // Обратная математика матрицы: достаем координаты X и Y из ID
//            int idx1 = id / baseLen;
//            int idx2 = id % baseLen;

//            res[resIdx] = clientAlphabet[idx1];
//            res[resIdx + 1] = clientAlphabet[idx2];
//            resIdx += 2;
//        }
//        return new string(res);
//    }

//    private bool IsCombineQueryRequest(VRCUrl value)
//    {
//        foreach (var rq in RunesQueries) if (rq == value) return true;

//        return false;
//    }
//    private VRCUrl QueryByRunesFrom(string runes)
//    {
//        foreach (var tQuery in RunesQueries) if (tQuery.Get() == runes) return tQuery;

//        return null;
//    }
//    private static VRCUrl[] GetQueries(string alphabet, string baseUrl)
//    {
//        switch (SizeChein)
//        {// then SizeChein will change ыыыыыыыыыыыы
//            case 3: return Gen4Url(alphabet, baseUrl);
//            case 4: return Gen4Url(alphabet, baseUrl);
//            default: return Gen(alphabet, baseUrl, SizeChein);
//        }
//    }

//    private static string IntArrayToString(int[] array)
//    {
//        if (array == null || array.Length == 0) return "";
//        char[] res = new char[array.Length];
//        for (int i = 0; i < array.Length; i++) res[i] = (char)array[i];
//        return new string(res);
//    }

//    private static VRCUrl[] Gen4Url(string alph, string baseUri)
//    {
//        int len = alph.Length, idx = 0;
//        VRCUrl[] res = new VRCUrl[len * len * len * len];

//        for (int i = 0; i < len; i++)
//            for (int j = 0; j < len; j++)
//                for (int k = 0; k < len; k++)
//                    for (int m = 0; m < len; m++)
//                        res[idx++] = new VRCUrl(baseUri + alph[i] + alph[j] + alph[k] + alph[m]);
//        return res;
//    }

//    private static VRCUrl[] Gen3Url(string alph, string baseUri)
//    {
//        int len = alph.Length, idx = 0;
//        VRCUrl[] res = new VRCUrl[len * len * len * len];

//        for (int i = 0; i < len; i++)
//            for (int j = 0; j < len; j++)
//                for (int k = 0; k < len; k++)
//                    res[idx++] = new VRCUrl(baseUri + alph[i] + alph[j] + alph[k]);
//        return res;
//    }

//    private static VRCUrl[] Gen(string alph, string baseUri, int gen)
//    {
//        if (string.IsNullOrEmpty(alph) || gen <= 0) return new VRCUrl[0];

//        int len = alph.Length, total = 1;
//        for (int i = 0; i < gen; i++) total *= len;

//        VRCUrl[] res = new VRCUrl[total];

//        for (int i = 0; i < total; i++)
//        {
//            string rune = "";
//            int temp = i;

//            for (int j = 0; j < gen; j++)
//            {
//                rune = alph[temp % len] + rune;
//                temp /= len;
//            }

//            res[i] = new VRCUrl(baseUri + rune);
//        }

//        return res;
//    }

//    private static string[] SplitStr(string s, int size)
//    {
//        if (string.IsNullOrEmpty(s) || size <= 0) return new string[0];

//        int count = s.Length / size, rem = s.Length % size, total = count + (rem > 0 ? 1 : 0);

//        string[] res = new string[total];

//        for (int i = 0; i < count; i++) res[i] = s.Substring(i * size, size);

//        if (rem > 0) res[total - 1] = s.Substring(count * size);

//        return res;
//    }
//}