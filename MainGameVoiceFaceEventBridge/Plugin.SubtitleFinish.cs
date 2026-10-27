using System;
using System.Reflection;

namespace MainGameVoiceFaceEventBridge
{
    internal sealed partial class Plugin
    {
        private static bool _subtitleFinishApiResolved;
        private static MethodInfo _subtitleFinishMethod;

        /// <summary>
        /// 返事を最後まで読み終えた時に字幕を消させる。
        ///
        /// 字幕側は「次の字幕が来るまで保持」で動いていて、読み終わりを知る手段が無い。
        /// キャンセルボタンからの停止と同じ仕組み（表示期限の切り詰め）を、自然終了でも使う。
        /// DLL参照ではなくリフレクションなので、SubtitleCoreが無くても壊れない。
        /// </summary>
        private void OnExternalVoiceSequenceFinished()
        {
            SetKaleidoscopeThought(false);
            _thoughtMarkedForSession = false;
            if (!_subtitleFinishApiResolved)
            {
                _subtitleFinishApiResolved = true;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        Type type = asm.GetType("MainGameSubtitleCore.Plugin", false);
                        if (type == null)
                            continue;

                        _subtitleFinishMethod = type.GetMethod(
                            "NotifyFemaleVoiceFinished",
                            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                            null,
                            new[] { typeof(string) },
                            null);
                        break;
                    }
                    catch
                    {
                    }
                }
            }

            if (_subtitleFinishMethod == null)
                return;

            try
            {
                _subtitleFinishMethod.Invoke(null, new object[] { "voice-finished" });
            }
            catch (Exception ex)
            {
                LogWarn("[subtitle] finish notify failed: " + ex.Message);
            }
        }
    }
}
