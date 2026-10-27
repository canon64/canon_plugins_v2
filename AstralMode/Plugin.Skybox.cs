using System;
using System.IO;
using BepInEx;
using UnityEngine;

namespace AstralMode
{
    public sealed partial class Plugin
    {
        private string _pluginDir;
        private AssetBundle _astralSkyboxBundle;
        private Material _astralSkyboxMaterial;
        private Material _originalSkybox;
        private bool _astralSkyboxApplied;

        private void InitAstralSkybox(string pluginDir)
        {
            _pluginDir = pluginDir;
            SimpleFileLogger.Info($"Astral skybox init: pluginDir={_pluginDir}, pathsPluginPath={Paths.PluginPath}, unityVersion={Application.unityVersion}, platform={Application.platform}, dataPath={Application.dataPath}");
        }

        private bool EnsureAstralSkyboxLoaded()
        {
            if (_astralSkyboxMaterial != null)
            {
                SimpleFileLogger.Info($"Astral skybox already loaded: material={DescribeMaterial(_astralSkyboxMaterial)}, bundle={DescribeBundle(_astralSkyboxBundle)}");
                return true;
            }

            if (_cfgUseAstralSkybox == null || !_cfgUseAstralSkybox.Value)
            {
                SimpleFileLogger.Info("Astral skybox disabled by config");
                return false;
            }

            string configuredPath = _cfgAstralSkyboxBundlePath != null
                ? _cfgAstralSkyboxBundlePath.Value
                : "astral_skybox/astral_skybox";
            if (string.IsNullOrEmpty(configuredPath))
            {
                SimpleFileLogger.Warn("Astral skybox bundle path is empty");
                return false;
            }

            string bundlePath = Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(_pluginDir ?? Paths.PluginPath, configuredPath);
            string fullBundlePath = Path.GetFullPath(bundlePath);

            SimpleFileLogger.Info($"Astral skybox resolve: configuredPath={configuredPath}, rooted={Path.IsPathRooted(configuredPath)}, resolvedPath={bundlePath}, fullPath={fullBundlePath}, pluginDir={_pluginDir}, pathsPluginPath={Paths.PluginPath}");

            if (!File.Exists(bundlePath))
            {
                SimpleFileLogger.Warn($"Astral skybox bundle not found: {bundlePath}");
                return false;
            }

            try
            {
                FileInfo fileInfo = new FileInfo(bundlePath);
                SimpleFileLogger.Info($"Astral skybox load start: path={bundlePath}, size={fileInfo.Length}, mtime={fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}, readonly={fileInfo.IsReadOnly}, unityVersion={Application.unityVersion}");
                LogAstralSkyboxFileHeader(bundlePath, fileInfo.Length);
                LogLoadedAssetBundles("before-load");

                var sw = System.Diagnostics.Stopwatch.StartNew();
                _astralSkyboxBundle = AssetBundle.LoadFromFile(bundlePath);
                sw.Stop();
                SimpleFileLogger.Info($"Astral skybox LoadFromFile result: ok={_astralSkyboxBundle != null}, elapsedMs={sw.ElapsedMilliseconds}, bundle={DescribeBundle(_astralSkyboxBundle)}");

                if (_astralSkyboxBundle == null)
                {
                    SimpleFileLogger.Warn($"Astral skybox LoadFromFile failed, trying LoadFromMemory: {bundlePath}");
                    byte[] bytes = File.ReadAllBytes(bundlePath);
                    SimpleFileLogger.Info($"Astral skybox memory read: bytes={bytes.Length}, header={FormatBytes(bytes, 0, Math.Min(bytes.Length, 32))}, ascii={FormatAscii(bytes, 0, Math.Min(bytes.Length, 32))}");

                    sw.Restart();
                    _astralSkyboxBundle = AssetBundle.LoadFromMemory(bytes);
                    sw.Stop();
                    SimpleFileLogger.Info($"Astral skybox LoadFromMemory result: ok={_astralSkyboxBundle != null}, elapsedMs={sw.ElapsedMilliseconds}, bundle={DescribeBundle(_astralSkyboxBundle)}");
                }

                if (_astralSkyboxBundle == null)
                {
                    SimpleFileLogger.Warn($"Astral skybox bundle load failed: {bundlePath}. header check above decides whether this is a real UnityFS AssetBundle. If header is UnityFS but both loaders return null, rebuild AssetBundle with the same Unity version as KKS. runtimeUnity={Application.unityVersion}");
                    LogLoadedAssetBundles("after-failed-load");
                    return false;
                }

                LogLoadedAssetBundles("after-load");

                string[] assetNames = _astralSkyboxBundle.GetAllAssetNames();
                string[] scenePaths = _astralSkyboxBundle.GetAllScenePaths();
                SimpleFileLogger.Info($"Astral skybox bundle opened: {DescribeBundle(_astralSkyboxBundle)}, assetCount={(assetNames != null ? assetNames.Length : -1)}, assets={JoinSafe(assetNames)}, sceneCount={(scenePaths != null ? scenePaths.Length : -1)}, scenes={JoinSafe(scenePaths)}");

                string materialName = _cfgAstralSkyboxMaterialName != null
                    ? _cfgAstralSkyboxMaterialName.Value
                    : "AstralSkybox";
                SimpleFileLogger.Info($"Astral skybox material lookup start: configuredName={materialName}");

                if (!string.IsNullOrEmpty(materialName))
                {
                    _astralSkyboxMaterial = _astralSkyboxBundle.LoadAsset<Material>(materialName);
                    SimpleFileLogger.Info($"Astral skybox material lookup direct: name={materialName}, ok={_astralSkyboxMaterial != null}, material={DescribeMaterial(_astralSkyboxMaterial)}");

                    if (_astralSkyboxMaterial == null)
                    {
                        string fallbackPath = $"assets/scenes/astralskybox/{materialName.ToLowerInvariant()}.mat";
                        _astralSkyboxMaterial = _astralSkyboxBundle.LoadAsset<Material>(fallbackPath);
                        SimpleFileLogger.Info($"Astral skybox material lookup path fallback: path={fallbackPath}, ok={_astralSkyboxMaterial != null}, material={DescribeMaterial(_astralSkyboxMaterial)}");
                    }
                }

                Material[] materials = _astralSkyboxBundle.LoadAllAssets<Material>();
                SimpleFileLogger.Info($"Astral skybox material inventory: count={(materials != null ? materials.Length : -1)}, materials={DescribeMaterials(materials)}");

                if (_astralSkyboxMaterial == null && materials != null && materials.Length > 0)
                {
                    _astralSkyboxMaterial = materials[0];
                    SimpleFileLogger.Info($"Astral skybox material selected by inventory fallback: {DescribeMaterial(_astralSkyboxMaterial)}");
                }

                if (_astralSkyboxMaterial == null)
                {
                    SimpleFileLogger.Warn($"Astral skybox material not found in bundle: {bundlePath}. requested={materialName}, assets={JoinSafe(assetNames)}");
                    _astralSkyboxBundle.Unload(false);
                    _astralSkyboxBundle = null;
                    return false;
                }

                ApplyAstralSkyboxMaterialSettings();
                SimpleFileLogger.Info($"Astral skybox loaded: bundle={bundlePath}, material={DescribeMaterial(_astralSkyboxMaterial)}");
                return true;
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"Astral skybox load exception: type={ex.GetType().FullName}, message={ex.Message}, stack={ex.StackTrace}");
                if (_astralSkyboxBundle != null)
                {
                    SimpleFileLogger.Info($"Astral skybox unloading after exception: {DescribeBundle(_astralSkyboxBundle)}");
                    _astralSkyboxBundle.Unload(false);
                    _astralSkyboxBundle = null;
                }
                _astralSkyboxMaterial = null;
                LogLoadedAssetBundles("after-exception");
                return false;
            }
        }

        private void ApplyAstralSkybox()
        {
            if (_astralSkyboxApplied)
            {
                SimpleFileLogger.Info($"Astral skybox apply skipped: already applied, current={DescribeMaterial(RenderSettings.skybox)}, astral={DescribeMaterial(_astralSkyboxMaterial)}, original={DescribeMaterial(_originalSkybox)}");
                return;
            }
            if (!EnsureAstralSkyboxLoaded())
            {
                SimpleFileLogger.Info($"Astral skybox apply skipped: load unavailable, current={DescribeMaterial(RenderSettings.skybox)}");
                return;
            }

            ApplyAstralSkyboxMaterialSettings();
            _originalSkybox = RenderSettings.skybox;
            SimpleFileLogger.Info($"Astral skybox applying: original={DescribeMaterial(_originalSkybox)}, astral={DescribeMaterial(_astralSkyboxMaterial)}");
            RenderSettings.skybox = _astralSkyboxMaterial;
            DynamicGI.UpdateEnvironment();
            _astralSkyboxApplied = true;
            SimpleFileLogger.Info($"Astral skybox applied: current={DescribeMaterial(RenderSettings.skybox)}, originalSaved={DescribeMaterial(_originalSkybox)}");
        }

        private void RestoreAstralSkybox()
        {
            if (!_astralSkyboxApplied)
            {
                SimpleFileLogger.Info($"Astral skybox restore skipped: not applied, current={DescribeMaterial(RenderSettings.skybox)}, originalSaved={DescribeMaterial(_originalSkybox)}");
                return;
            }

            SimpleFileLogger.Info($"Astral skybox restoring: current={DescribeMaterial(RenderSettings.skybox)}, restoreTo={DescribeMaterial(_originalSkybox)}");
            RenderSettings.skybox = _originalSkybox;
            DynamicGI.UpdateEnvironment();
            _originalSkybox = null;
            _astralSkyboxApplied = false;
            SimpleFileLogger.Info($"Astral skybox restored: current={DescribeMaterial(RenderSettings.skybox)}");
        }

        private void UnloadAstralSkybox()
        {
            SimpleFileLogger.Info($"Astral skybox unload start: applied={_astralSkyboxApplied}, bundle={DescribeBundle(_astralSkyboxBundle)}, material={DescribeMaterial(_astralSkyboxMaterial)}");
            RestoreAstralSkybox();
            if (_astralSkyboxBundle != null)
            {
                _astralSkyboxBundle.Unload(false);
                _astralSkyboxBundle = null;
                SimpleFileLogger.Info("Astral skybox bundle unloaded: unloadAllLoadedObjects=false");
            }
            _astralSkyboxMaterial = null;
            LogLoadedAssetBundles("after-unload");
        }

        private static void LogAstralSkyboxFileHeader(string path, long length)
        {
            try
            {
                byte[] header = new byte[Math.Min(length, 64)];
                using (FileStream fs = File.OpenRead(path))
                {
                    int read = fs.Read(header, 0, header.Length);
                    SimpleFileLogger.Info($"Astral skybox file header: read={read}, hex={FormatBytes(header, 0, read)}, ascii={FormatAscii(header, 0, read)}, looksUnityFS={StartsWithAscii(header, read, "UnityFS")}, looksUnityRaw={StartsWithAscii(header, read, "UnityRaw")}, looksUnityWeb={StartsWithAscii(header, read, "UnityWeb")}");
                }
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"Astral skybox file header read failed: type={ex.GetType().FullName}, message={ex.Message}");
            }
        }

        private static void LogLoadedAssetBundles(string phase)
        {
            try
            {
                var bundles = AssetBundle.GetAllLoadedAssetBundles();
                int count = 0;
                string names = string.Empty;
                foreach (AssetBundle b in bundles)
                {
                    if (count > 0) names += ", ";
                    names += DescribeBundle(b);
                    count++;
                    if (count >= 20)
                    {
                        names += ", ...";
                        break;
                    }
                }
                SimpleFileLogger.Info($"Astral skybox loaded bundles {phase}: countAtLeast={count}, bundles={names}");
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"Astral skybox loaded bundles log failed: phase={phase}, type={ex.GetType().FullName}, message={ex.Message}");
            }
        }

        private static string DescribeBundle(AssetBundle bundle)
        {
            if (bundle == null) return "null";
            try
            {
                return $"name={bundle.name}, streamedScene={bundle.isStreamedSceneAssetBundle}";
            }
            catch (Exception ex)
            {
                return $"bundle-describe-error:{ex.GetType().Name}:{ex.Message}";
            }
        }

        private static string DescribeMaterial(Material mat)
        {
            if (mat == null) return "null";
            string shaderName = "null";
            try { shaderName = mat.shader != null ? mat.shader.name : "null"; } catch { shaderName = "error"; }
            return $"name={mat.name}, shader={shaderName}, instId={mat.GetInstanceID()}";
        }

        private static string DescribeMaterials(Material[] materials)
        {
            if (materials == null) return "null";
            if (materials.Length == 0) return "empty";
            string s = string.Empty;
            int max = Math.Min(materials.Length, 20);
            for (int i = 0; i < max; i++)
            {
                if (i > 0) s += " | ";
                s += DescribeMaterial(materials[i]);
            }
            if (materials.Length > max) s += " | ...";
            return s;
        }

        private static string JoinSafe(string[] values)
        {
            if (values == null) return "null";
            if (values.Length == 0) return "empty";
            return string.Join(", ", values);
        }

        private static string FormatBytes(byte[] bytes, int offset, int count)
        {
            if (bytes == null) return "null";
            if (count <= 0) return "empty";
            int end = Math.Min(bytes.Length, offset + count);
            string s = string.Empty;
            for (int i = offset; i < end; i++)
            {
                if (i > offset) s += " ";
                s += bytes[i].ToString("X2");
            }
            return s;
        }

        private static string FormatAscii(byte[] bytes, int offset, int count)
        {
            if (bytes == null) return "null";
            if (count <= 0) return "empty";
            int end = Math.Min(bytes.Length, offset + count);
            char[] chars = new char[end - offset];
            for (int i = offset; i < end; i++)
            {
                byte b = bytes[i];
                chars[i - offset] = b >= 32 && b <= 126 ? (char)b : '.';
            }
            return new string(chars);
        }

        private static bool StartsWithAscii(byte[] bytes, int count, string text)
        {
            if (bytes == null || text == null || count < text.Length) return false;
            for (int i = 0; i < text.Length; i++)
            {
                if (bytes[i] != (byte)text[i]) return false;
            }
            return true;
        }
    }
}
