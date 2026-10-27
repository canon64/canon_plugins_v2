using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class LiquidShaderBuild
{
    public static void Build()
    {
        string buildError = null;
        Application.LogCallback captureError = (message, stack, type) => {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert ||
                message.IndexOf("is not supported because the module", StringComparison.Ordinal) >= 0)
                buildError = message;
        };
        Application.logMessageReceived += captureError;
        try
        {
            const string asset = "Assets/MilkLiquid.shader";
            AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(asset);
            if (shader == null) throw new InvalidOperationException("Liquid shader import failed.");
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            string output = Path.Combine(root, "obj", "ShaderBundle");
            Directory.CreateDirectory(output);
            var build = new AssetBundleBuild { assetBundleName = "milkliquid.bundle", assetNames = new[] { asset } };
            var manifest = BuildPipeline.BuildAssetBundles(output, new[] { build },
                BuildAssetBundleOptions.ForceRebuildAssetBundle | BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
                BuildTarget.StandaloneWindows64);
            if (buildError != null) throw new InvalidOperationException("Liquid bundle build reported an error: " + buildError);
            if (manifest == null) throw new InvalidOperationException("Liquid shader bundle build failed.");
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
                if (message.severity.ToString() == "Error") throw new InvalidOperationException(message.message);
            string resources = Path.Combine(root, "Resources");
            Directory.CreateDirectory(resources);
            File.Copy(Path.Combine(output, "milkliquid.bundle"), Path.Combine(resources, "milkliquid.bundle"), true);
            Debug.Log("MILK_LIQUID_SHADER_BUILD_OK");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
        finally { Application.logMessageReceived -= captureError; }
    }
}
