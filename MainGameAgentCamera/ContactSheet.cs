using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace MainGameAgentCamera
{
    /// <summary>
    /// ランダムな角度で複数枚撮り、番号を焼き込んだ一覧画像にまとめる。
    ///
    /// 狙い: 20案を1枚にすれば、選ぶ側は画像を1回見るだけで全部を比較できる。
    /// 20回撮って20回見るのに比べて桁違いに速く、かつ横並びで比べられる。
    ///
    /// 全カットを同じフレームで撮る。ポーズは動き続けているので、
    /// 1枚ずつ間を空けると「同じ瞬間の撮り比べ」にならない。
    /// </summary>
    internal static class ContactSheet
    {
        internal sealed class Candidate
        {
            internal int Index;
            internal string Path = string.Empty;
            internal string Target = string.Empty;
            internal float Yaw;
            internal float Pitch;
            internal float Roll;
            internal float Distance;
            internal float Fov;

            // 案ごとに狙う部位が違ってよいので、注視点と基準軸も個別に持つ。
            internal Vector3 LookAt;
            internal Vector3 FrameUp;
            internal Vector3 FrameFront;
        }

        internal sealed class Result
        {
            internal bool Ok;
            internal string Error = string.Empty;
            internal string SheetPath = string.Empty;
            internal readonly List<Candidate> Candidates = new List<Candidate>();
        }

        internal static Result Build(
            CameraControl_Ver2 ctrl,
            List<Candidate> candidates,
            int columns,
            int cellWidth,
            int cellHeight,
            int fullWidth,
            int fullHeight,
            string baseName,
            string outputDir,
            Action<string> log)
        {
            Result result = new Result();
            Camera cam = CameraRig.ResolveCamera(ctrl);
            if (cam == null)
            {
                result.Error = "camera not found";
                return result;
            }

            int count = candidates != null ? candidates.Count : 0;
            if (count <= 0)
            {
                result.Error = "no shots given";
                return result;
            }
            columns = Mathf.Clamp(columns, 1, 8);
            int rows = Mathf.CeilToInt(count / (float)columns);

            // 撮り終えたら必ず元へ戻す。撮影のために動かしただけなので。
            BaseCameraControl_Ver2.CameraData restore = ctrl.GetCameraData();
            float restoreFov = restore.Fov;

            RenderTexture cellRt = null;
            RenderTexture fullRt = null;
            Texture2D cellTex = null;
            Texture2D fullTex = null;
            Texture2D sheet = null;
            RenderTexture prevActive = RenderTexture.active;
            RenderTexture prevTarget = cam.targetTexture;
            float prevAspect = cam.aspect;
            float prevFov = cam.fieldOfView;

            try
            {
                Directory.CreateDirectory(outputDir);

                cellRt = RenderTexture.GetTemporary(cellWidth, cellHeight, 24, RenderTextureFormat.ARGB32);
                fullRt = RenderTexture.GetTemporary(fullWidth, fullHeight, 24, RenderTextureFormat.ARGB32);
                cellTex = new Texture2D(cellWidth, cellHeight, TextureFormat.RGB24, false);
                fullTex = new Texture2D(fullWidth, fullHeight, TextureFormat.RGB24, false);
                sheet = new Texture2D(cellWidth * columns, cellHeight * rows, TextureFormat.RGB24, false);

                for (int i = 0; i < count; i++)
                {
                    Candidate c = candidates[i];
                    c.Index = i + 1;
                    Vector3 pos = CameraRig.OrbitPosition(
                        c.LookAt, c.FrameUp, c.FrameFront, c.Yaw, c.Pitch, c.Distance);
                    // 画面の上は常に地面基準。案ごとに転ばせない。
                    CameraRig.ApplyFraming(ctrl, c.LookAt, pos, Vector3.up, c.Roll, c.Fov);

                    // 一覧用の小さい絵
                    RenderInto(cam, cellRt, cellTex, cellWidth, cellHeight, c.Fov);

                    // 番号を焼く。選んだ後に「何番」で指定できるようにするため。
                    DrawNumber(cellTex, c.Index);

                    int col = i % columns;
                    int row = i / columns;
                    // Texture2D の原点は左下。一覧は左上から並べたいので行を反転する。
                    int destX = col * cellWidth;
                    int destY = (rows - 1 - row) * cellHeight;
                    sheet.SetPixels(destX, destY, cellWidth, cellHeight, cellTex.GetPixels());

                    // 本番用の大きい絵。後で番号を指定したらこれを送る。
                    // ポーズは動き続けるので、後から撮り直すと別の瞬間になる。今保存する。
                    RenderInto(cam, fullRt, fullTex, fullWidth, fullHeight, c.Fov);
                    string path = Path.Combine(
                        outputDir, baseName + "_" + c.Index.ToString("00", CultureInfo.InvariantCulture) + ".png");
                    File.WriteAllBytes(path, fullTex.EncodeToPNG());
                    c.Path = path;
                    result.Candidates.Add(c);
                }

                result.SheetPath = Path.Combine(outputDir, baseName + "_sheet.png");
                File.WriteAllBytes(result.SheetPath, sheet.EncodeToPNG());
                result.Ok = true;
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                if (log != null) log("sheet failed: " + ex);
                return result;
            }
            finally
            {
                cam.targetTexture = prevTarget;
                cam.aspect = prevAspect;
                cam.fieldOfView = prevFov;
                RenderTexture.active = prevActive;
                if (cellRt != null) RenderTexture.ReleaseTemporary(cellRt);
                if (fullRt != null) RenderTexture.ReleaseTemporary(fullRt);
                if (cellTex != null) UnityEngine.Object.Destroy(cellTex);
                if (fullTex != null) UnityEngine.Object.Destroy(fullTex);
                if (sheet != null) UnityEngine.Object.Destroy(sheet);

                ctrl.SetCameraData(restore);
                CameraRig.ApplyFov(ctrl, restoreFov);
            }
        }

        private static void RenderInto(
            Camera cam, RenderTexture rt, Texture2D dest, int width, int height, float fov)
        {
            cam.targetTexture = rt;
            cam.aspect = width / (float)height;
            if (fov > 0f) cam.fieldOfView = Mathf.Clamp(fov, 5f, 120f);
            cam.Render();
            RenderTexture.active = rt;
            dest.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        }

        // ---- 番号の焼き込み ----
        // 外部フォントに頼らず、3x5 の点で数字を描く。
        // 一覧から「何番」を指定するためだけなので、読めれば十分。
        private static readonly byte[][] Digits =
        {
            new byte[] { 0x7, 0x5, 0x5, 0x5, 0x7 }, // 0
            new byte[] { 0x2, 0x6, 0x2, 0x2, 0x7 }, // 1
            new byte[] { 0x7, 0x1, 0x7, 0x4, 0x7 }, // 2
            new byte[] { 0x7, 0x1, 0x7, 0x1, 0x7 }, // 3
            new byte[] { 0x5, 0x5, 0x7, 0x1, 0x1 }, // 4
            new byte[] { 0x7, 0x4, 0x7, 0x1, 0x7 }, // 5
            new byte[] { 0x7, 0x4, 0x7, 0x5, 0x7 }, // 6
            new byte[] { 0x7, 0x1, 0x1, 0x1, 0x1 }, // 7
            new byte[] { 0x7, 0x5, 0x7, 0x5, 0x7 }, // 8
            new byte[] { 0x7, 0x5, 0x7, 0x1, 0x7 }, // 9
        };

        private static void DrawNumber(Texture2D tex, int number)
        {
            string text = number.ToString(CultureInfo.InvariantCulture);
            int scale = Mathf.Max(3, tex.height / 40);
            int digitW = 3 * scale;
            int gap = scale;
            int pad = scale * 2;

            int boxW = text.Length * (digitW + gap) + pad * 2;
            int boxH = 5 * scale + pad * 2;

            // 下地を暗く塗る。明るい背景でも番号が読めるように。
            int originX = pad;
            int originY = tex.height - boxH - pad;
            FillRect(tex, originX, originY, boxW, boxH, new Color(0f, 0f, 0f, 1f));

            int x = originX + pad;
            int y = originY + pad;
            for (int i = 0; i < text.Length; i++)
            {
                int d = text[i] - '0';
                if (d < 0 || d > 9) continue;
                DrawDigit(tex, Digits[d], x, y, scale);
                x += digitW + gap;
            }

        }

        private static void DrawDigit(Texture2D tex, byte[] glyph, int x, int y, int scale)
        {
            // glyph は上の行から並んでいる。Texture2D は下が原点なので行を反転して置く。
            for (int row = 0; row < 5; row++)
            {
                byte bits = glyph[row];
                for (int col = 0; col < 3; col++)
                {
                    bool on = (bits & (1 << (2 - col))) != 0;
                    if (!on) continue;
                    FillRect(tex, x + col * scale, y + (4 - row) * scale, scale, scale, Color.white);
                }
            }
        }

        private static void FillRect(Texture2D tex, int x, int y, int w, int h, Color color)
        {
            int x0 = Mathf.Clamp(x, 0, tex.width);
            int y0 = Mathf.Clamp(y, 0, tex.height);
            int x1 = Mathf.Clamp(x + w, 0, tex.width);
            int y1 = Mathf.Clamp(y + h, 0, tex.height);
            int width = x1 - x0;
            int height = y1 - y0;
            if (width <= 0 || height <= 0) return;

            Color[] block = new Color[width * height];
            for (int i = 0; i < block.Length; i++) block[i] = color;
            tex.SetPixels(x0, y0, width, height, block);
        }

        internal static string CandidatesToJson(List<Candidate> candidates)
        {
            StringBuilder sb = new StringBuilder(512);
            sb.Append('[');
            for (int i = 0; i < candidates.Count; i++)
            {
                Candidate c = candidates[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"index\":").Append(c.Index)
                  .Append(",\"yaw\":").Append(F(c.Yaw))
                  .Append(",\"pitch\":").Append(F(c.Pitch))
                  .Append(",\"roll\":").Append(F(c.Roll))
                  .Append(",\"distance\":").Append(F(c.Distance))
                  .Append(",\"fov\":").Append(F(c.Fov))
                  .Append(",\"target\":\"").Append(c.Target ?? string.Empty).Append('"')
                  .Append(",\"path\":\"").Append(c.Path.Replace("\\", "\\\\")).Append("\"}");
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string F(float v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
