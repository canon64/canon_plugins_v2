using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using UnityEngine;

namespace MainGameUiInputCapture
{
    public sealed class InputBindingDefinition
    {
        public string Id, Plugin, Title, Default, Description, Source, Kind, Config, Section, Key, JsonKey, Status;
        public bool Exact;
        internal string Baseline;
    }

    /// <summary>Shared action bindings. Existing per-plugin settings remain the baseline until overridden here.</summary>
    public static partial class InputBindingsApi
    {
        [Serializable] public sealed class SavedBinding { public string id; public string input; }
        [Serializable] public sealed class SavedBindings { public int version = 1; public List<SavedBinding> bindings = new List<SavedBinding>(); }
        internal sealed class Chord
        {
            internal string Main;
            internal readonly List<string> Modifiers = new List<string>();
        }
        internal static readonly List<InputBindingDefinition> Definitions = InputBindingCatalog.Create();
        private static readonly Dictionary<string, InputBindingDefinition> ById = Definitions.ToDictionary(x => x.Id, StringComparer.Ordinal);
        private static readonly Dictionary<string, string> Overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, List<Chord>> Parsed = new Dictionary<string, List<Chord>>(StringComparer.Ordinal);
        private static readonly Dictionary<Type, MethodInfo[]> VrMethods = new Dictionary<Type, MethodInfo[]>();
        private static bool _loaded;
        private static string _path;
        internal static string Error = string.Empty;
        private static readonly KeyCode[] AllKeys = (KeyCode[])Enum.GetValues(typeof(KeyCode));
        internal static Func<KeyCode, bool> KeyHeld = Input.GetKey;
        internal static Func<KeyCode, bool> KeyDown = Input.GetKeyDown;
        internal static Func<KeyCode, bool> KeyUp = Input.GetKeyUp;
        internal static Func<string, float> ReadAxis = Input.GetAxisRaw;

        public static void Initialize(string directory)
        {
            if (_loaded) return;
            _path = Path.Combine(directory, "InputBindings.json");
            Reload();
        }

        private static void EnsureLoaded()
        {
            if (!_loaded) Initialize(Path.Combine(Paths.PluginPath, "canon_plugins", "MainGameUiInputCapture"));
        }

        public static void Reload()
        {
            _loaded = true;
            Overrides.Clear();
            Parsed.Clear();
            Error = string.Empty;
            foreach (var d in Definitions) d.Baseline = ReadBaseline(d);
            if (!File.Exists(_path)) return;
            try
            {
                var saved = JsonUtility.FromJson<SavedBindings>(File.ReadAllText(_path));
                if (saved == null || saved.version != 1 || saved.bindings == null) throw new InvalidDataException("割当ファイルの形式が不正です。");
                foreach (var item in saved.bindings)
                {
                    if (item == null || item.id == null || !ById.TryGetValue(item.id, out var d)) continue;
                    if (Validate(d, item.input, out var error)) Overrides[item.id] = item.input;
                    else Error += d.Title + ": " + error + " ";
                }
            }
            catch (Exception ex) { Error = "割当を読み込めません: " + ex.Message; }
        }

        private static string ReadBaseline(InputBindingDefinition d)
        {
            if (string.IsNullOrEmpty(d.Config)) return d.Default;
            string path = Path.Combine(Paths.BepInExRootPath, d.Config.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                if (!File.Exists(path)) return d.Default;
                string text = File.ReadAllText(path);
                if (!string.IsNullOrEmpty(d.JsonKey))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(text, "\"" + System.Text.RegularExpressions.Regex.Escape(d.JsonKey) + "\"\\s*:\\s*\"([^\"]*)\"");
                    return match.Success ? match.Groups[1].Value : d.Default;
                }
                string section = string.Empty;
                foreach (string raw in text.Split('\n'))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("[")) { section = line.Trim('[', ']'); continue; }
                    if (section != d.Section || line.StartsWith("#")) continue;
                    int equals = line.IndexOf('=');
                    if (equals >= 0 && line.Substring(0, equals).Trim() == d.Key) return line.Substring(equals + 1).Trim();
                }
            }
            catch (Exception ex) { Error = "元設定を読めません: " + d.Plugin + " " + ex.Message; }
            return d.Default;
        }

        internal static string Resolve(string id, string fallback = null)
        {
            EnsureLoaded();
            if (ById.TryGetValue(id, out var d) && fallback != null) d.Baseline = fallback;
            string resolved = Overrides.TryGetValue(id, out var value) ? value : d?.Baseline ?? fallback ?? "None";
            return resolved == "Not set" ? "None" : resolved;
        }

        internal static bool Set(string id, string value, bool remove = false)
        {
            EnsureLoaded();
            if (!ById.TryGetValue(id, out var d)) { Error = "未登録の操作: " + id; return false; }
            if (!remove && !Validate(d, value, out Error)) return false;
            bool hadOld = Overrides.TryGetValue(id, out var old);
            if (remove) Overrides.Remove(id); else Overrides[id] = value.Trim();
            try
            {
                var saved = new SavedBindings();
                foreach (var pair in Overrides.OrderBy(x => x.Key, StringComparer.Ordinal)) saved.bindings.Add(new SavedBinding { id = pair.Key, input = pair.Value });
                string temporary = _path + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(saved, true));
                if (File.Exists(_path)) File.Replace(temporary, _path, null); else File.Move(temporary, _path);
                Parsed.Clear();
                Error = string.Empty;
                _suppressUntilFrame = Time.frameCount + 1;
                return true;
            }
            catch (Exception ex)
            {
                if (hadOld) Overrides[id] = old; else Overrides.Remove(id);
                Error = "保存できません: " + ex.Message;
                return false;
            }
        }

        internal static bool Validate(InputBindingDefinition d, string value, out string error)
        {
            error = string.Empty;
            if (value == null) { error = "None または入力名を指定してください。"; return false; }
            if (!TryParse(value, d.Kind, out _, out error)) return false;
            return true;
        }

        internal static bool TryParse(string value, string kind, out List<Chord> chords, out string error)
        {
            chords = new List<Chord>(); error = string.Empty;
            if (string.IsNullOrWhiteSpace(value) || value.Trim().Equals("None", StringComparison.OrdinalIgnoreCase) || value.Trim().Equals("Not set", StringComparison.OrdinalIgnoreCase)) return true;
            foreach (string alternative in value.Split('|'))
            {
                var tokens = alternative.Split('+').Select(x => kind == "vr" ? x.Trim() : NormalizeToken(x.Trim())).ToArray();
                if (tokens.Any(string.IsNullOrEmpty)) { error = "空のキーがあります。例: Mouse2 + Ctrl"; return false; }
                var c = new Chord();
                if (kind == "vr")
                {
                    if (!TryVrButton(tokens[0], out _)) { error = "VRボタン: Grip / Trigger / Axis0 / Axis1 / A / ApplicationMenu / System / 数値0〜63"; return false; }
                    c.Main = tokens[0];
                    foreach (string token in tokens.Skip(1))
                    {
                        if (!TryVrButton(token, out _)) { error = "不正なVR修飾ボタン: " + token; return false; }
                        c.Modifiers.Add(token);
                    }
                }
                else
                {
                    foreach (string token in tokens)
                    {
                        if (IsModifier(token)) c.Modifiers.Add(token);
                        else if (c.Main == null && IsMain(token, kind)) c.Main = token;
                        else if (c.Main != null && Enum.TryParse(token, true, out KeyCode extra) && Enum.IsDefined(typeof(KeyCode), extra) && extra != KeyCode.None) c.Modifiers.Add(token);
                        else { error = "不正な入力: " + token + "。複数候補は | で区切ります。"; return false; }
                    }
                    if (c.Main == null && c.Modifiers.Count > 0 && kind != "axis" && kind != "stick") { c.Main = c.Modifiers[0]; c.Modifiers.RemoveAt(0); }
                    if (c.Main == null) { error = "主キーが必要です。"; return false; }
                }
                chords.Add(c);
            }
            return true;
        }

        private static bool IsMain(string token, string kind)
        {
            if (kind == "stick") return token == "StickX" || token == "StickY" || token == "-StickX" || token == "-StickY";
            if (kind == "axis") return token == "Wheel" || token == "MouseX" || token == "MouseY";
            return token == "Wheel" || (Enum.TryParse(token, true, out KeyCode code) && Enum.IsDefined(typeof(KeyCode), code) && code != KeyCode.None);
        }

        private static string NormalizeToken(string token)
        {
            switch (token.ToLowerInvariant())
            {
                case "ctrl": case "control": return "Ctrl";
                case "shift": return "Shift";
                case "alt": return "Alt";
                case "wheel": return "Wheel";
                case "mousex": return "MouseX";
                case "mousey": return "MouseY";
                case "stickx": return "StickX";
                case "sticky": return "StickY";
                case "-stickx": return "-StickX";
                case "-sticky": return "-StickY";
                default: return Enum.TryParse(token, true, out KeyCode code) ? code.ToString() : token;
            }
        }

        private static bool IsModifier(string name)
        {
            return name == "Ctrl" || name == "Control" || name == "Shift" || name == "Alt"
                || name == "LeftControl" || name == "RightControl" || name == "LeftShift" || name == "RightShift" || name == "LeftAlt" || name == "RightAlt";
        }

        private static bool ReadKey(string name, int phase)
        {
            Func<KeyCode, bool> read = phase == 1 ? KeyDown : phase == 2 ? KeyUp : KeyHeld;
            if (name == "Ctrl" || name == "Control") return read(KeyCode.LeftControl) || read(KeyCode.RightControl);
            if (name == "Shift") return read(KeyCode.LeftShift) || read(KeyCode.RightShift);
            if (name == "Alt") return read(KeyCode.LeftAlt) || read(KeyCode.RightAlt);
            if (name == "Wheel") return Mathf.Abs(ReadAxis("Mouse ScrollWheel")) > 0.0001f;
            return Enum.TryParse(name, true, out KeyCode code) && read(code);
        }

        private static List<Chord> GetChords(string id, string fallback)
        {
            string value = Resolve(id, fallback);
            string kind = ById.TryGetValue(id, out var d) ? d.Kind : "key";
            string cacheKey = kind + ":" + value;
            if (!Parsed.TryGetValue(cacheKey, out var chords))
            {
                TryParse(value, kind, out chords, out _);
                Parsed[cacheKey] = chords;
            }
            return chords;
        }

        public static bool Down(string id, string fallback, bool exclusiveMouse = false) => Read(id, fallback, 1, false, exclusiveMouse);
        public static bool Held(string id, string fallback, bool exclusiveMouse = false) => Read(id, fallback, 0, false, exclusiveMouse);
        public static bool MainHeld(string id, string fallback) => Read(id, fallback, 0, true, false);
        public static bool AllowsTextSubmit(string id, string fallback)
        {
            return !InputSuppressed && GetChords(id, fallback).Any(c =>
                c.Main == "Return" && c.Modifiers.All(x => ReadKey(x, 0)));
        }
        private static bool Read(string id, string fallback, int phase, bool mainOnly, bool exclusiveMouse)
        {
            if (InputSuppressed) return false;
            foreach (var chord in GetChords(id, fallback))
            {
                if (!ReadKey(chord.Main, phase) || (!mainOnly && !chord.Modifiers.All(x => ReadKey(x, 0)))) continue;
                if (!mainOnly && ById.TryGetValue(id, out var definition) && definition.Exact && HasExtraKeys(chord)) continue;
                // Plain 5 must not also handle the Shift+5 clothing shortcut.
                if (!mainOnly && id == "MainGameVMDPlay.liquid" && (KeyHeld(KeyCode.LeftShift) || KeyHeld(KeyCode.RightShift))
                    && chord.Main != "Shift" && chord.Main != "LeftShift" && chord.Main != "RightShift"
                    && !chord.Modifiers.Any(x => x == "Shift" || x == "LeftShift" || x == "RightShift")) continue;
                if (exclusiveMouse && Enumerable.Range(0, 3).Any(i => KeyHeld((KeyCode)((int)KeyCode.Mouse0 + i)) && !string.Equals(chord.Main, "Mouse" + i, StringComparison.OrdinalIgnoreCase) && !chord.Modifiers.Contains("Mouse" + i))) continue;
                return true;
            }
            return false;
        }

        private static bool HasExtraKeys(Chord chord)
        {
            foreach (KeyCode code in AllKeys)
            {
                if (code == KeyCode.None || ((int)code >= (int)KeyCode.Mouse0 && (int)code <= (int)KeyCode.Mouse0 + 6) || !KeyHeld(code)) continue;
                string name = code.ToString();
                if (chord.Main == name || chord.Modifiers.Contains(name)) continue;
                if ((code == KeyCode.LeftControl || code == KeyCode.RightControl) && (chord.Main == "Ctrl" || chord.Modifiers.Contains("Ctrl"))) continue;
                if ((code == KeyCode.LeftShift || code == KeyCode.RightShift) && (chord.Main == "Shift" || chord.Modifiers.Contains("Shift"))) continue;
                if ((code == KeyCode.LeftAlt || code == KeyCode.RightAlt) && (chord.Main == "Alt" || chord.Modifiers.Contains("Alt"))) continue;
                return true;
            }
            return false;
        }

        public static float Axis(string id, string fallback)
        {
            if (InputSuppressed) return 0f;
            foreach (var c in GetChords(id, fallback))
            {
                if (!c.Modifiers.All(x => ReadKey(x, 0))) continue;
                string axis = c.Main == "MouseX" ? "Mouse X" : c.Main == "MouseY" ? "Mouse Y" : "Mouse ScrollWheel";
                float value = ReadAxis(axis);
                if (Mathf.Abs(value) > 0.0001f) return value;
            }
            return 0f;
        }

        public static float Stick(string id, Vector2 axis)
        {
            if (InputSuppressed) return 0f;
            foreach (var c in GetChords(id, "StickY"))
                if (c.Modifiers.All(x => ReadKey(x, 0))) return (c.Main.EndsWith("X", StringComparison.Ordinal) ? axis.x : axis.y) * (c.Main.StartsWith("-", StringComparison.Ordinal) ? -1f : 1f);
            return 0f;
        }

        internal static bool TryVrButton(string name, out int value)
        {
            string n = name.Replace("k_EButton_", "").Replace("SteamVR_", "");
            switch (n.ToLowerInvariant())
            {
                case "system": value = 0; return true;
                case "applicationmenu": case "menu": value = 1; return true;
                case "grip": value = 2; return true;
                case "a": value = 7; return true;
                case "axis0": case "touchpad": case "trackpad": value = 32; return true;
                case "axis1": case "trigger": value = 33; return true;
                case "axis2": value = 34; return true;
                case "axis3": value = 35; return true;
                case "axis4": value = 36; return true;
                default: return int.TryParse(n, out value) && value >= 0 && value < 64;
            }
        }

        public static bool Vr(string id, object input, int phase, string fallback)
        {
            if (input == null) return false;
            // Callers ask for release only while already grabbing. Editing or
            // changing/clearing a binding must release that grab as well.
            if (InputSuppressed) return phase == 2;
            if (!VrMethods.TryGetValue(input.GetType(), out var methods))
            {
                methods = new[] { "GetPress", "GetPressDown", "GetPressUp" }.Select(name => input.GetType().GetMethods().FirstOrDefault(m => m.Name == name && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.IsEnum)).ToArray();
                VrMethods[input.GetType()] = methods;
            }
            if (phase < 0 || phase > 2 || methods[0] == null || (phase != 2 && methods[phase] == null)) return false;
            var chords = GetChords(id, fallback);
            if (phase == 2) return !chords.Any(c => ReadVr(input, methods[0], c.Main) && c.Modifiers.All(x => ReadVr(input, methods[0], x)));
            foreach (var c in chords)
            {
                if (!ReadVr(input, methods[phase], c.Main)) continue;
                if (c.Modifiers.All(x => ReadVr(input, methods[0], x))) return true;
            }
            return false;
        }

        private static bool ReadVr(object input, MethodInfo method, string name)
        {
            if (method == null || !TryVrButton(name, out int value)) return false;
            return (bool)method.Invoke(input, new[] { Enum.ToObject(method.GetParameters()[0].ParameterType, value) });
        }
    }
}
