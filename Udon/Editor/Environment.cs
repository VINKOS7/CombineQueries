using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

// Tools > CombineQueries > Environment
//
// The client bakes its whole VRCUrl pool from one const baseUrl, so a world can only ever talk to
// one server. Which one is chosen at compile time by the CQ_ALPHA define:
//
//   dev    (no define)  -> DevUrl,   the server you run locally
//   alpha  (CQ_ALPHA)   -> AlphaUrl, the server a published build talks to
//
// Switching flips the define for the ACTIVE build target, Unity recompiles UdonSharp, and the pool
// is rebaked on the next world load. Check the mark here before every publish - a world built in
// dev points at localhost and will look broken to everyone but you.
public static class Environment
{
    private const string Define = "CQ_ALPHA";

    [MenuItem("Tools/CombineQueries/Environment/Use dev")]
    private static void UseDev() => Set(false);

    [MenuItem("Tools/CombineQueries/Environment/Use alpha")]
    private static void UseAlpha() => Set(true);

    [MenuItem("Tools/CombineQueries/Environment/Use dev", true)]
    private static bool DevMark() { Menu.SetChecked("Tools/CombineQueries/Environment/Use dev", !IsAlpha()); return true; }

    [MenuItem("Tools/CombineQueries/Environment/Use alpha", true)]
    private static bool AlphaMark() { Menu.SetChecked("Tools/CombineQueries/Environment/Use alpha", IsAlpha()); return true; }

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

    private static bool IsAlpha() => System.Array.IndexOf(Defines, Define) >= 0;

    private static void Set(bool alpha)
    {
        if (alpha == IsAlpha())
        {
            Debug.Log("[CombineQueries] environment is already " + (alpha ? "alpha" : "dev"));
            return;
        }

        var defines = new System.Collections.Generic.List<string>(Defines);

        if (alpha) defines.Add(Define);
        else defines.Remove(Define);

        PlayerSettings.SetScriptingDefineSymbols(Target, defines.ToArray());

        Debug.Log("[CombineQueries] environment -> " + (alpha ? "alpha (AlphaUrl)" : "dev (DevUrl)")
                + ". UdonSharp recompiles, the url pool is rebaked on the next world load.");
    }
}
