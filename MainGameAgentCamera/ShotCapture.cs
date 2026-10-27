using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MainGameAgentCamera
{
    /// <summary>
    /// カメラの絵を PNG に落とす。
    ///
    /// カメラを RenderTexture へ描かせる方式なので、IMGUI の窓や uGUI のオーバーレイは写らない。
    /// UI を隠す小細工が要らないのはそのため。
    /// </summary>
    internal static class ShotCapture
    {
        /// <summary>
        /// 撮影の間だけ隠した画面パネル。撮り終えたら必ず戻す。
        /// </summary>
        private static readonly List<Renderer> HiddenScreens = new List<Renderer>();

        /// <summary>
        /// 部屋の壁などに貼られた「カメラ映像を映している板」を撮影中だけ消す。
        ///
        /// 名前で列挙すると環境ごとに破綻するので、表示している絵が RenderTexture か
        /// どうかで機械的に判別する。映像パネルは必ず RenderTexture を貼っている。
        /// </summary>
        private static void HideScreenPanels(Camera capturing)
        {
            HiddenScreens.Clear();
            Renderer[] renderers = UnityEngine.Object.FindObjectsOfType<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null || !r.enabled) continue;

                // キャラクター本体は絶対に消さない。
                // KKS は肌や顔のテクスチャを実行時に RenderTexture へ合成するので、
                // 「RenderTexture を貼っている」だけで判定すると人間まで消える。
                if (r.GetComponentInParent<ChaControl>() != null) continue;

                Material[] materials = r.sharedMaterials;
                if (materials == null) continue;

                bool showsCameraFeed = false;
                for (int m = 0; m < materials.Length; m++)
                {
                    Material mat = materials[m];
                    if (mat == null) continue;
                    if (mat.mainTexture is RenderTexture) { showsCameraFeed = true; break; }
                }

                if (!showsCameraFeed) continue;
                r.enabled = false;
                HiddenScreens.Add(r);
            }
        }

        private static void RestoreScreenPanels()
        {
            for (int i = 0; i < HiddenScreens.Count; i++)
            {
                if (HiddenScreens[i] != null) HiddenScreens[i].enabled = true;
            }
            HiddenScreens.Clear();
        }

        internal static bool TryCapture(
            Camera camera, int width, int height, string path, float fov, bool hideScreens, out string error)
        {
            error = string.Empty;
            if (camera == null)
            {
                error = "camera not found";
                return false;
            }

            width = Mathf.Clamp(width, 64, 4096);
            height = Mathf.Clamp(height, 64, 4096);

            RenderTexture rt = null;
            Texture2D texture = null;
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            float previousAspect = camera.aspect;
            float previousFov = camera.fieldOfView;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

                if (hideScreens) HideScreenPanels(camera);

                rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
                camera.targetTexture = rt;
                camera.aspect = width / (float)height;

                // 画角は撮る直前に入れる。他のプラグインが毎フレーム上書きしていても、
                // Render() は同期実行なので、この代入と描画の間には誰も割り込めない。
                if (fov > 0f) camera.fieldOfView = Mathf.Clamp(fov, 5f, 120f);

                camera.Render();

                RenderTexture.active = rt;
                texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);

                File.WriteAllBytes(path, texture.EncodeToPNG());
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                // 借りたものは順番通りに返す。ここを落とすと画面が壊れたままになる。
                camera.targetTexture = previousTarget;
                camera.aspect = previousAspect;
                camera.fieldOfView = previousFov;
                RenderTexture.active = previousActive;
                RestoreScreenPanels();
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (texture != null) UnityEngine.Object.Destroy(texture);
            }
        }
    }
}
