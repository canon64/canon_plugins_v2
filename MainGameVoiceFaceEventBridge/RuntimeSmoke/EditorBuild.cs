using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ImpactCueSmokeBuild
{
    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("ImpactCue runtime smoke").AddComponent<RuntimeSmoke>();
        EditorSceneManager.SaveScene(scene, "Assets/Smoke.unity");
        var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Smoke.unity" },
            locationPathName = "../Player/ImpactCueSmoke.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });
        if (result.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new System.Exception("Smoke player build failed: " + result.summary.result);
    }
}
