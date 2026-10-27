using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace MainGameAgentCamera
{
    /// <summary>
    /// 部位ごとの撮り方の既定値。
    ///
    /// 以前はソースに直接書いていたが、数値を1つ変えるたびに
    /// ビルドとゲーム再起動が必要になり、構図を詰める作業にならなかった。
    /// JSON に出して、ファイルを保存した時点で反映されるようにする。
    /// </summary>
    [Serializable]
    internal sealed class TargetTuning
    {
        /// <summary>face / bust / whole など。look の target に対応する。</summary>
        public string name = string.Empty;

        /// <summary>体(または顔)の正面を0度とした水平角。+で被写体から見て右へ回り込む。</summary>
        public float yaw;
        /// <summary>仰角。+で見下ろし、-で煽り。</summary>
        public float pitch;
        /// <summary>カメラの傾き。</summary>
        public float roll;
        /// <summary>注視点からの距離(m)。autoFit が有効なら、これより引くことはある。</summary>
        public float distance = 1f;
        /// <summary>画角。小さいほど望遠。</summary>
        public float fov = 35f;
        /// <summary>画面端に残す余白の割合。小さいほど画面いっぱいになる。</summary>
        public float margin = 0.06f;

        /// <summary>注視点を体の上方向へずらす量(m)。頭の骨は首の付け根にあるので顔用に使う。</summary>
        public float lookAtRaise;

        /// <summary>収まり判定に使う箱の半分の幅・上・下(m)。</summary>
        public float boxWidth = 0.15f;
        public float boxUp = 0.15f;
        public float boxDown = 0.15f;

        /// <summary>顔の向きを基準に角度を取るか。うつむいた顔を正面から撮るために使う。</summary>
        public bool useHeadFrame;
    }

    [Serializable]
    internal sealed class AgentCameraSettings
    {
        internal const string FileName = "AgentCameraSettings.json";

        /// <summary>target 未指定のときに使う部位。</summary>
        public string defaultTarget = "whole";
        /// <summary>撮影対象の女性の番号。</summary>
        public int femaleIndex = 0;
        /// <summary>capture の既定サイズ。</summary>
        public int captureWidth = 1080;
        public int captureHeight = 1920;
        /// <summary>hold の最長保持秒数。掴みっぱなしを防ぐため必ず自動解放する。</summary>
        public float maxHoldSeconds = 120f;
        /// <summary>コマンドフォルダを見に行く間隔(秒)。</summary>
        public float pollSeconds = 0.1f;

        /// <summary>whole の余白。体の箱がこの余白を残して画面に収まる。</summary>
        public float wholeMargin = 0.03f;
        /// <summary>whole の既定 yaw / pitch / fov。</summary>
        public float wholeYaw = 25f;
        public float wholePitch = 5f;
        public float wholeFov = 40f;

        /// <summary>
        /// 判定用の控え画像で、カメラ映像の板を消すか。
        /// キャラクターの体は消さない（KKSは肌もRenderTextureで作るため、
        /// 素直に判定すると人間まで消える。実測で確認済み）。
        /// </summary>
        public bool hideScreensInPreview = true;

        public TargetTuning[] targets = new TargetTuning[0];

        internal TargetTuning Find(string name)
        {
            if (targets == null || string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < targets.Length; i++)
            {
                TargetTuning t = targets[i];
                if (t != null && string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase))
                    return t;
            }
            return null;
        }

        /// <summary>初回に書き出す内容。今までソースに埋めていた値をそのまま出す。</summary>
        internal static AgentCameraSettings CreateDefault()
        {
            List<TargetTuning> list = new List<TargetTuning>
            {
                new TargetTuning { name = "face",   yaw = 25f, pitch = 10f, distance = 0.45f, fov = 30f, margin = 0.05f, lookAtRaise = 0.10f, boxWidth = 0.085f, boxUp = 0.115f, boxDown = 0.105f, useHeadFrame = true },
                new TargetTuning { name = "head",   yaw = 25f, pitch = 8f,  distance = 0.70f, fov = 32f, margin = 0.06f, lookAtRaise = 0.08f, boxWidth = 0.11f,  boxUp = 0.14f,  boxDown = 0.14f,  useHeadFrame = true },
                new TargetTuning { name = "neck",   yaw = 35f, pitch = 12f, distance = 0.80f, fov = 34f, margin = 0.06f, boxWidth = 0.14f, boxUp = 0.16f, boxDown = 0.14f },
                new TargetTuning { name = "bust",   yaw = 20f, pitch = 18f, distance = 0.60f, fov = 32f, margin = 0.06f, boxWidth = 0.16f, boxUp = 0.14f, boxDown = 0.14f },
                new TargetTuning { name = "chest",  yaw = 20f, pitch = 10f, distance = 0.90f, fov = 35f, margin = 0.06f, boxWidth = 0.18f, boxUp = 0.16f, boxDown = 0.16f },
                new TargetTuning { name = "waist",  yaw = 30f, pitch = 8f,  distance = 0.90f, fov = 35f, margin = 0.06f, boxWidth = 0.18f, boxUp = 0.16f, boxDown = 0.16f },
                new TargetTuning { name = "belly",  yaw = 25f, pitch = 10f, distance = 1.00f, fov = 35f, margin = 0.06f, boxWidth = 0.18f, boxUp = 0.16f, boxDown = 0.16f },
                new TargetTuning { name = "hips",   yaw = 30f, pitch = 10f, distance = 1.00f, fov = 35f, margin = 0.06f, boxWidth = 0.20f, boxUp = 0.18f, boxDown = 0.18f },
                new TargetTuning { name = "butt",   yaw = 150f, pitch = 20f, distance = 0.70f, fov = 33f, margin = 0.06f, boxWidth = 0.18f, boxUp = 0.16f, boxDown = 0.16f },
                new TargetTuning { name = "crotch", yaw = 0f,  pitch = -15f, distance = 0.80f, fov = 35f, margin = 0.06f, boxWidth = 0.18f, boxUp = 0.16f, boxDown = 0.16f },
                new TargetTuning { name = "upper",  yaw = 20f, pitch = 5f,  distance = 1.20f, fov = 35f, margin = 0.07f, boxWidth = 0.26f, boxUp = 0.20f, boxDown = 0.20f },
            };

            return new AgentCameraSettings { targets = list.ToArray() };
        }
    }

    /// <summary>
    /// 設定の読み書き。ファイルが更新されていれば読み直す。
    /// ゲームを起動したまま数値を詰められるようにするための仕掛け。
    /// </summary>
    internal static class SettingsStore
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private static AgentCameraSettings _cached;
        private static string _path;
        private static DateTime _stamp;
        private static float _nextCheckTime;

        internal static AgentCameraSettings Current(string pluginDir, Action<string> log)
        {
            if (_path == null)
                _path = Path.Combine(pluginDir, AgentCameraSettings.FileName);

            // 毎コマンドで stat するのは無駄なので、少し間隔を空けて確認する。
            if (_cached != null && Time.unscaledTime < _nextCheckTime)
                return _cached;
            _nextCheckTime = Time.unscaledTime + 1.0f;

            try
            {
                if (!File.Exists(_path))
                {
                    _cached = AgentCameraSettings.CreateDefault();
                    Save(log);
                    return _cached;
                }

                DateTime stamp = File.GetLastWriteTimeUtc(_path);
                if (_cached != null && stamp == _stamp)
                    return _cached;

                string text = File.ReadAllText(_path, Utf8NoBom);
                AgentCameraSettings loaded = JsonUtility.FromJson<AgentCameraSettings>(text);
                if (loaded == null)
                {
                    if (log != null) log("settings parse failed; keeping previous values");
                    if (_cached == null) _cached = AgentCameraSettings.CreateDefault();
                    return _cached;
                }

                _stamp = stamp;
                _cached = loaded;
                if (log != null) log("settings reloaded: " + _path);
                return _cached;
            }
            catch (Exception ex)
            {
                if (log != null) log("settings load failed: " + ex.Message);
                if (_cached == null) _cached = AgentCameraSettings.CreateDefault();
                return _cached;
            }
        }

        internal static void Save(Action<string> log)
        {
            if (_path == null || _cached == null) return;
            try
            {
                File.WriteAllText(_path, JsonUtility.ToJson(_cached, true), Utf8NoBom);
                _stamp = File.GetLastWriteTimeUtc(_path);
                if (log != null) log("settings written: " + _path);
            }
            catch (Exception ex)
            {
                if (log != null) log("settings save failed: " + ex.Message);
            }
        }
    }
}
