using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class KaleidoscopeShaderBuild
{
    public static void Build()
    {
        try
        {
            const string asset = "Assets/Kaleidoscope.shader";
            AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(asset);
            if (shader == null) throw new InvalidOperationException("Shader import failed");
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
                if (message.severity.ToString() == "Error") throw new InvalidOperationException(message.message);
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            string buildDir = Path.Combine(root, "obj", "ShaderBundle");
            Directory.CreateDirectory(buildDir);
            var build = new AssetBundleBuild { assetBundleName = "kaleidoscope.bundle", assetNames = new[] { asset } };
            var manifest = BuildPipeline.BuildAssetBundles(buildDir, new[] { build },
                BuildAssetBundleOptions.ForceRebuildAssetBundle | BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
                BuildTarget.StandaloneWindows64);
            if (manifest == null) throw new InvalidOperationException("Shader bundle build failed");
            string resourceDir = Path.Combine(root, "Resources");
            Directory.CreateDirectory(resourceDir);
            File.Copy(Path.Combine(buildDir, "kaleidoscope.bundle"), Path.Combine(resourceDir, "kaleidoscope.bundle"), true);
            AssetBundle bundle = AssetBundle.LoadFromFile(Path.Combine(resourceDir, "kaleidoscope.bundle"));
            if (bundle == null || bundle.LoadAsset<Shader>("assets/kaleidoscope.shader") == null)
                throw new InvalidOperationException("Shader missing from built bundle");
            bundle.Unload(false);
            Debug.Log("KALEIDOSCOPE_SHADER_BUILD_OK");
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
}
