using System;
using System.Reflection;
using UnityEngine;

namespace CanonPlugins.Shared
{
    /// <summary>
    /// ホットキー処理の手前に置く門番。
    /// MainGameSubtitleCore が立てた表札(MainGameSubtitleCore.TextInputState.IsTextInputActive)を読み、
    /// 文字入力中はキーを自分のものとして解釈しない。
    ///
    /// 入力欄で押した Enter が他プラグインのトグルとして誤爆するのを止めるためのもの。
    /// 表札を下ろした直後は字幕側が猶予を持たせているので、フレーム内の Update 順は問わない。
    ///
    /// このファイルは実体を1つだけ持ち、各 csproj から Compile Include でリンク参照する。
    /// 型は internal なので、各アセンブリに別々の型として畳み込まれても衝突しない。
    ///
    /// BlankRoomGate と違い、表札が見つからない場合は false（開く側）に倒す。
    /// 字幕プラグインが入っていないなら文字入力そのものが無く、
    /// ホットキーはこれまで通り効くべきだから。
    /// </summary>
    internal static class TextInputGate
    {
        private const string StateTypeName = "MainGameSubtitleCore.TextInputState";
        private const string StatePropertyName = "IsTextInputActive";

        // 字幕プラグインがまだ読み込まれていない可能性があるので、見つかるまで間隔をあけて再挑戦する。
        private const int ResolveRetryFrames = 120;

        private static Func<bool> _reader;
        private static int _nextResolveFrame;

        private static int _cachedFrame = -1;
        private static bool _cachedValue;

        /// <summary>入力パネルで文字を打っている最中なら true。ホットキーは見送ること。</summary>
        internal static bool IsTextInputActive
        {
            get
            {
                int frame = Time.frameCount;
                if (_cachedFrame == frame) return _cachedValue;
                _cachedFrame = frame;
                _cachedValue = ReadState();
                return _cachedValue;
            }
        }

        private static bool ReadState()
        {
            if (_reader != null) return _reader();

            if (Time.frameCount < _nextResolveFrame) return false;
            _nextResolveFrame = Time.frameCount + ResolveRetryFrames;

            return TryResolveReader() && _reader();
        }

        private static bool TryResolveReader()
        {
            try
            {
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length; i++)
                {
                    Type type;
                    try
                    {
                        type = assemblies[i].GetType(StateTypeName, false);
                    }
                    catch
                    {
                        continue;
                    }
                    if (type == null) continue;

                    PropertyInfo property = type.GetProperty(
                        StatePropertyName, BindingFlags.Public | BindingFlags.Static);
                    MethodInfo getter = property != null ? property.GetGetMethod() : null;
                    if (getter == null) continue;

                    _reader = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), getter);
                    Debug.Log($"[TextInputGate] bound to {assemblies[i].GetName().Name}.{StateTypeName}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TextInputGate] resolve failed: {ex.Message}");
            }

            return false;
        }
    }
}
