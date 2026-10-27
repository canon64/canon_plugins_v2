using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace CanonPlugins.Shared
{
    /// <summary>
    /// 各プラグインの入口に置く門番。
    /// BlankMapAdd が立てた表札(MainGameBlankMapAdd.BlankRoomState.IsInBlankRoom)を読み、
    /// 自作の部屋にいないあいだは処理を通さない。
    ///
    /// このファイルは実体を1つだけ持ち、各 csproj から Compile Include でリンク参照する。
    /// 型は internal なので、各アセンブリに別々の型として畳み込まれても衝突しない。
    ///
    /// 判定コスト対策として、1フレームにつき1回だけ表札を読み、同フレーム内は結果を使い回す。
    /// 表札への参照は起動時に一度だけ解決してデリゲート化する(毎回の reflection を避ける)。
    /// </summary>
    internal static class BlankRoomGate
    {
        private const string StateTypeName = "MainGameBlankMapAdd.BlankRoomState";
        private const string StatePropertyName = "IsInBlankRoom";
        private const string ConfigFileName = "BlankRoomGate.json";

        // BlankMapAdd がまだ読み込まれていない可能性があるので、見つかるまで間隔をあけて再挑戦する。
        private const int ResolveRetryFrames = 120;

        private static Func<bool> _reader;
        private static int _nextResolveFrame;
        private static bool _missingLogged;

        private static int _cachedFrame = -1;
        private static bool _cachedValue;

        private static bool _restrictLoaded;
        private static bool _restrict = true;

        /// <summary>
        /// 設定で制限が切られている場合は常に true(従来どおり全部屋で動く)。
        /// 制限が有効なら、追加マップ滞在中だけ true。表札が見つからない場合は false(閉じる側に倒す)。
        /// </summary>
        internal static bool IsInBlankRoom
        {
            get
            {
                if (!RestrictToBlankRoom) return true;

                int frame = Time.frameCount;
                if (_cachedFrame == frame) return _cachedValue;
                _cachedFrame = frame;
                _cachedValue = ReadState();
                return _cachedValue;
            }
        }

        /// <summary>
        /// 毎フレームの入口で使う定型。部屋の中なら true を返すのでそのまま処理を続ける。
        /// 部屋の外なら false を返し、退室した最初の1回だけ <paramref name="onLeave"/> を呼ぶ。
        ///
        /// 使い方:
        ///   if (!BlankRoomGate.Tick(ref _gateWasInRoom, CloseEverything)) return;
        /// </summary>
        internal static bool Tick(ref bool wasInRoom, Action onLeave)
        {
            bool inRoom = IsInBlankRoom;

            if (inRoom)
            {
                wasInRoom = true;
                return true;
            }

            if (wasInRoom)
            {
                wasInRoom = false;
                if (onLeave != null)
                {
                    try
                    {
                        onLeave();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[BlankRoomGate] leave handler failed: {ex.Message}");
                    }
                }
            }

            return false;
        }

        /// <summary>この部屋以外で発火させない制限そのものの ON/OFF。プラグインごとの JSON で切れる。</summary>
        internal static bool RestrictToBlankRoom
        {
            get
            {
                if (!_restrictLoaded)
                {
                    _restrictLoaded = true;
                    _restrict = LoadRestrictSetting();
                }
                return _restrict;
            }
        }

        private static bool ReadState()
        {
            if (_reader != null) return _reader();

            if (Time.frameCount < _nextResolveFrame) return false;
            _nextResolveFrame = Time.frameCount + ResolveRetryFrames;

            if (!TryResolveReader())
            {
                if (!_missingLogged)
                {
                    _missingLogged = true;
                    Debug.LogWarning(
                        "[BlankRoomGate] MainGameBlankMapAdd.BlankRoomState not found. "
                        + "Treating as 'outside the blank room' (this plugin stays silent). "
                        + $"Set RestrictToBlankRoom=false in {ConfigFileName} to run everywhere.");
                }
                return false;
            }

            return _reader();
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
                    Debug.Log($"[BlankRoomGate] bound to {assemblies[i].GetName().Name}.{StateTypeName}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BlankRoomGate] resolve failed: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// 自分の DLL と同じフォルダの JSON を読む。無ければ既定値(制限あり)で作る。
        /// 依存を増やさないため、必要な1項目だけを素朴に読み取る。
        /// </summary>
        private static bool LoadRestrictSetting()
        {
            try
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(dir)) return true;

                string path = Path.Combine(dir, ConfigFileName);
                if (!File.Exists(path))
                {
                    File.WriteAllText(
                        path,
                        // false にすると、この部屋以外でもこれまで通り動く。
                        "{\n  \"RestrictToBlankRoom\": true\n}\n",
                        new System.Text.UTF8Encoding(false));
                    return true;
                }

                string text = File.ReadAllText(path, new System.Text.UTF8Encoding(false));
                int key = text.IndexOf("\"RestrictToBlankRoom\"", StringComparison.OrdinalIgnoreCase);
                if (key < 0) return true;

                int colon = text.IndexOf(':', key);
                if (colon < 0) return true;

                string tail = text.Substring(colon + 1);
                int end = tail.IndexOfAny(new[] { ',', '}', '\r', '\n' });
                if (end >= 0) tail = tail.Substring(0, end);

                bool value;
                return !bool.TryParse(tail.Trim(), out value) || value;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BlankRoomGate] config load failed: {ex.Message}");
                return true;
            }
        }
    }
}
