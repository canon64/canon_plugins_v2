using System;
using System.IO;
using UnityEngine;

namespace MainGameSubCameraDisplayProbe
{
    public sealed partial class Plugin
    {
        private void CaptureSubCameraPhoto(string source)
        {
            EnsureCameraProbe();
            if (_subCamera == null || _renderTexture == null)
            {
                LogWarn("photo capture skipped: camera or renderTexture missing source=" + source);
                SetStatus("写真保存失敗: サブカメラなし");
                return;
            }

            RenderTexture previousActive = RenderTexture.active;
            Texture2D texture = null;
            try
            {
                _subCamera.Render();
                RenderTexture.active = _renderTexture;

                texture = new Texture2D(_renderTexture.width, _renderTexture.height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0f, 0f, _renderTexture.width, _renderTexture.height), 0, 0);

                byte[] png = texture.EncodeToPNG();
                if (png == null || png.Length == 0)
                    throw new InvalidOperationException("EncodeToPNG returned empty data");

                string outputDir = EnsurePhotoOutputDirectory();
                string fileName = "SubCamera_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png";
                string path = Path.Combine(outputDir, fileName);
                File.WriteAllBytes(path, png);

                LogInfo("photo saved source=" + source + " path=" + path);
                SetStatus("写真保存: " + fileName);
            }
            catch (Exception ex)
            {
                LogWarn("photo capture failed source=" + source + " ex=" + ex.Message);
                SetStatus("写真保存失敗: " + ex.Message);
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (texture != null)
                    Destroy(texture);
            }
        }

        private string EnsurePhotoOutputDirectory()
        {
            string outputDir = ResolvePhotoOutputDir();
            try
            {
                Directory.CreateDirectory(outputDir);
            }
            catch (Exception ex)
            {
                LogWarn("photo output directory create failed path=" + outputDir + " ex=" + ex.Message);
            }
            return outputDir;
        }

        private string ResolvePhotoOutputDir()
        {
            string folder = _settings != null && !string.IsNullOrWhiteSpace(_settings.PhotoOutputFolder)
                ? _settings.PhotoOutputFolder.Trim()
                : "image";

            if (Path.IsPathRooted(folder))
                return folder;

            return Path.Combine(_pluginDir ?? string.Empty, folder);
        }
    }
}
