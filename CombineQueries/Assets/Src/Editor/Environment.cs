// Environment toggle (Tools > CombineQueries > Environment > Use release / Use dev) is disabled for now.
// Set the target manually instead: Project Settings > Player > Scripting Define Symbols - add CQ_RELEASE
// for the published (release) build, remove it for local dev. Uncomment below to bring the menu back.

/*
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public static class Environment
{
    private const string Define = "CQ_RELEASE";

    [MenuItem("Tools/CombineQueries/Environment/Use dev")]
    private static void UseDev() => Set(false);

    [MenuItem("Tools/CombineQueries/Environment/Use release")]
    private static void UseAlpha() => Set(true);

    [MenuItem("Tools/CombineQueries/Environment/Use dev", true)]
    private static bool DevMark() { Menu.SetChecked("Tools/CombineQueries/Environment/Use dev", !IsAlpha()); return true; }

    [MenuItem("Tools/CombineQueries/Environment/Use release", true)]
    private static bool AlphaMark() { Menu.SetChecked("Tools/CombineQueries/Environment/Use release", IsAlpha()); return true; }

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
*/
