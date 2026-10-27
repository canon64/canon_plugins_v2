using System;

namespace MainGameVoiceFaceEventBridge
{
    [Serializable]
    internal sealed class ClothesItem
    {
        public int kind  = -1;
        // -2=2段目ずらし, -1=1段目ずらし, 0=着用, 1/2=ずらし, 3=脱衣
        public int state = -1;
    }

    [Serializable]
    internal sealed class ExternalVoiceSequenceItem
    {
        public int index = -1;
        public string audioPath = string.Empty;
        public string path = string.Empty;
        public string subtitle = string.Empty;
        public string text = string.Empty;
        public float durationSeconds = 0f;
        public float holdSeconds = 0f;
        public int deleteAfterPlay = -1;

        internal string ResolveAudioPath()
        {
            if (!string.IsNullOrWhiteSpace(audioPath))
            {
                return audioPath;
            }

            return path ?? string.Empty;
        }

        internal string ResolveSubtitle()
        {
            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                return subtitle;
            }

            return text ?? string.Empty;
        }
    }

    [Serializable]
    internal sealed class ExternalVoiceFaceCommand
    {
        public string type = "speak";
        public string sessionId = string.Empty;

        // coord コマンド用: 衣装名で検索して着替える
        public string coordName = string.Empty;

        // clothes コマンド用: 各部位の着衣状態を変える
        public ClothesItem[] clothesItems = null;

        // response_text コマンド用: 生テキストをC#側でパースして着替え/着衣検出
        public string text = string.Empty;
        // speak_sequence 用: 音声シーケンスと同梱されたトリガー解析用テキスト。
        public string responseText = string.Empty;
        public float delaySeconds = 0f;
        public string traceId = string.Empty;
        public string source = string.Empty;
        public int preferNamedPoseOverPrompt = 0;
        public int skipNamedPose = 0;

        // response_text の行ごとタイミング用（任意）。行テキストと行ごと実尺(秒)。
        // 両方そろい件数一致なら、トリガー発火時刻を「行ごと実尺」ベースで算出する。
        public string[] lineTexts = null;
        public float[] lineDurations = null;
        public int lineIndexOffset = 0;
        // Original Grok lines survive TTS chunking. -1 means no score on this line.
        public int[] lineSourceIndices = null;
        public string[] lineSourceTexts = null;
        public int[] lineMovementIntensities = null;

        // pose コマンド用: nameAnimation で体位を切り替える
        // poseMode: -1=any, 0=aibu, 1=houshi, 2=sonyu, 3=masturbation, 4=peeping, 5=lesbian, 6=houshi3P, 7=sonyu3P
        public string poseName = string.Empty;
        public int poseMode = -1;

        // semantic_pose コマンド用: 自然文から類似ポーズを検索して適用する
        public string posePrompt = string.Empty;
        public int captureAfterApply = 0;
        public int captureCount = 0;

        // camera_preset コマンド用: MainGameCameraControl の保存プリセットを呼ぶ
        public string cameraPresetName = string.Empty;
        public int cameraPresetIndex = -1;

        public string audioPath = string.Empty;
        public string path = string.Empty;
        public ExternalVoiceSequenceItem[] items = null;

        public int main = -1;
        public string assetBundle = string.Empty;
        public string bundle = string.Empty;
        public string assetName = string.Empty;
        public string asset = string.Empty;
        public float pitch = -1f;
        public float fadeTime = -1f;
        public int eyeNeck = -1;
        public int voiceKind = -1;
        public int action = -1;
        public int interrupt = -1;
        public int deleteAfterPlay = -1;
        public float volume = -1f;

        // -1: use default setting, 0: force face-select mode, 1: keep current face mode.
        public int keepCurrentFace = -1;
        // Optional alias ("keep_current" / "select")
        public string faceMode = string.Empty;

        // FacePresetTool routing (name/id/random selection on receiver)
        public string facePresetId = string.Empty;
        public string facePresetName = string.Empty;
        // -1: unset, 0: false, 1: true
        public int facePresetRandom = -1;

        public int face = -1;
        public int[] faces = null;

        internal static ExternalVoiceFaceCommand FromPlainAssetName(string rawAssetName)
        {
            return new ExternalVoiceFaceCommand
            {
                type = "speak",
                assetName = rawAssetName ?? string.Empty
            };
        }

        internal static ExternalVoiceFaceCommand FromPlainAudioPath(string rawPath)
        {
            return new ExternalVoiceFaceCommand
            {
                type = "speak",
                audioPath = rawPath ?? string.Empty
            };
        }

        internal bool IsStop()
        {
            return string.Equals(type, "stop", StringComparison.OrdinalIgnoreCase);
        }

        internal string ResolveAudioPath()
        {
            if (!string.IsNullOrWhiteSpace(audioPath))
            {
                return audioPath;
            }

            return path ?? string.Empty;
        }

        internal int ResolveMain(int defaultValue)
        {
            return main < 0 ? defaultValue : main;
        }

        internal string ResolveAssetBundle(string defaultValue)
        {
            if (!string.IsNullOrWhiteSpace(assetBundle))
            {
                return assetBundle;
            }

            if (!string.IsNullOrWhiteSpace(bundle))
            {
                return bundle;
            }

            return defaultValue ?? string.Empty;
        }

        internal string ResolveAssetName(string defaultValue)
        {
            if (!string.IsNullOrWhiteSpace(assetName))
            {
                return assetName;
            }

            if (!string.IsNullOrWhiteSpace(asset))
            {
                return asset;
            }

            return defaultValue ?? string.Empty;
        }

        internal float ResolvePitch(float defaultValue)
        {
            float value = pitch < 0f ? defaultValue : pitch;
            if (value < 0f)
            {
                return 0f;
            }

            if (value > 3f)
            {
                return 3f;
            }

            return value;
        }

        internal float ResolveFadeTime(float defaultValue)
        {
            float value = fadeTime < 0f ? defaultValue : fadeTime;
            if (value < 0f)
            {
                return 0f;
            }

            return value;
        }

        internal int ResolveEyeNeck(int defaultValue)
        {
            return eyeNeck < 0 ? defaultValue : eyeNeck;
        }

        internal int ResolveVoiceKind(int defaultValue)
        {
            return voiceKind < 0 ? defaultValue : voiceKind;
        }

        internal int ResolveAction(int defaultValue)
        {
            return action < 0 ? defaultValue : action;
        }

        internal bool ResolveInterrupt(bool defaultValue)
        {
            if (interrupt < 0)
            {
                return defaultValue;
            }

            return interrupt != 0;
        }

        internal bool ResolveDeleteAfterPlay(bool defaultValue)
        {
            if (deleteAfterPlay < 0)
            {
                return defaultValue;
            }

            return deleteAfterPlay != 0;
        }

        internal float ResolveVolume(float defaultValue)
        {
            float value = volume < 0f ? defaultValue : volume;
            if (value < 0f)
            {
                return 0f;
            }

            if (value > 1f)
            {
                return 1f;
            }

            return value;
        }

        internal bool ResolveKeepCurrentFace(bool defaultValue)
        {
            if (keepCurrentFace >= 0)
            {
                return keepCurrentFace != 0;
            }

            if (!string.IsNullOrWhiteSpace(faceMode))
            {
                string mode = faceMode.Trim().ToLowerInvariant();
                if (mode == "keep_current" || mode == "keep" || mode == "current")
                {
                    return true;
                }

                if (mode == "select" || mode == "random" || mode == "fixed")
                {
                    return false;
                }
            }

            return defaultValue;
        }

        internal int ResolveFace(int defaultValue)
        {
            return face < 0 ? defaultValue : face;
        }

        internal string ResolveFacePresetId()
        {
            return facePresetId ?? string.Empty;
        }

        internal string ResolveFacePresetName()
        {
            return facePresetName ?? string.Empty;
        }

        internal bool ResolveFacePresetRandom(bool defaultValue)
        {
            if (facePresetRandom >= 0)
            {
                return facePresetRandom != 0;
            }

            return defaultValue;
        }
    }
}
