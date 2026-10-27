using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MainGameHScenePoseInjector
{
    public sealed partial class Plugin
    {
        // These channels are deliberately separate: the game's face liquid and tears
        // do not share storage with Additional Skin Effects.
        private const int Cheek = 0, GameTears = 1, FaceLiquid = 2,
            ModTears = 3, ModDrool = 4, ModSaliva = 5, ModSweat = 6, FrontUpLiquid = 7, FrontDownLiquid = 8, BackUpLiquid = 9, BackDownLiquid = 10, EffectChannelCount = 11;
        private static readonly string[] ModProperties = { null, null, null,
            "TearLevel", "DroolLevel", "SalivaLevel", "SweatLevel" };
        private static bool IsLiquidChannel(int channel) => channel == FaceLiquid || (channel >= FrontUpLiquid && channel <= BackDownLiquid);
        private static ChaFileDefine.SiruParts LiquidPart(int channel) => channel == FaceLiquid
            ? ChaFileDefine.SiruParts.SiruKao : (ChaFileDefine.SiruParts)(channel - FrontUpLiquid + 1);
        private static readonly string[] TextureCounts = { null, null, null,
            "TearTexturesCount", "DroolTexturesCount", "SalivaTexturesCount", "WetTexturesFaceCount" };

        private sealed class EffectTarget
        {
            public ChaControl Cha;
            public readonly bool[] ManualOverride = new bool[EffectChannelCount];
            public readonly int?[] Desired = new int?[EffectChannelCount];
        }

        private readonly Dictionary<int, EffectTarget> _effectTargets = new Dictionary<int, EffectTarget>();
        private readonly CycleClock[] _effectClocks = { new CycleClock(), new CycleClock(), new CycleClock(),
            new CycleClock(), new CycleClock(), new CycleClock(), new CycleClock(), new CycleClock(), new CycleClock(), new CycleClock(), new CycleClock() };
        private readonly List<ChaControl> _effectFemales = new List<ChaControl>();
        private int _selectedEffectCharacter;
        private HSceneProc _effectScene;
        private Type _skinEffectType;
        private Type _textureLoaderType;
        private bool _skinEffectSearched;
        private MethodInfo _readyIndependentActor;
        private float _nextIndependentApiSearch;
        private ChaControl _effectIndependentActor;
        private void AppendIndependentEffectTarget()
        {
            if (_readyIndependentActor == null && Time.unscaledTime >= _nextIndependentApiSearch) {
                _nextIndependentApiSearch = Time.unscaledTime + 1f;
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                    var api = assembly.GetType("MainGameVMDPlay.IndependentActorApi", false);
                    if (api == null) continue;
                    _readyIndependentActor = api.GetMethod("GetReadyActor", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                    break;
                }
            }
            _effectIndependentActor = _readyIndependentActor?.Invoke(null, null) as ChaControl;
            if (_effectIndependentActor != null && !_effectFemales.Contains(_effectIndependentActor))
                _effectFemales.Add(_effectIndependentActor);
        }
        private static readonly MethodInfo UpdateSiru = typeof(ChaControl).GetMethod("UpdateSiru",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(bool) }, null);

        private void RefreshEffectScene()
        {
            if (!IsInHScene())
            {
                _effectTargets.Clear();
                _effectScene = null;
                _selectedEffectCharacter = 0;
                return;
            }
            if (ReferenceEquals(_effectScene, _activeHSceneProc)) return;
            _effectTargets.Clear();
            _effectScene = _activeHSceneProc;
            _selectedEffectCharacter = 0;
            foreach (CycleClock clock in _effectClocks) { clock.NextAt = 0f; clock.Index = -1; }
        }

        private ChaControl SelectedEffectCharacter()
        {
            RefreshEffectScene();
            if (!IsInHScene()) return null;
            ResolveFemalesInto(_effectFemales);
            AppendIndependentEffectTarget();
            if (_effectFemales.Count == 0) return null;
            if (!_effectFemales.Exists(c => c != null && c.GetInstanceID() == _selectedEffectCharacter))
            {
                // A deleted/replaced explicit target must not redirect an active cycle to an H participant.
                if (_selectedEffectCharacter != 0) return null;
                _selectedEffectCharacter = _effectFemales[0].GetInstanceID();
                ReadEffectSettings(_effectFemales[0]);
            }
            return _effectFemales.Find(c => c != null && c.GetInstanceID() == _selectedEffectCharacter);
        }

        private EffectTarget StateFor(ChaControl cha)
        {
            int id = cha.GetInstanceID();
            if (!_effectTargets.TryGetValue(id, out EffectTarget state) || !ReferenceEquals(state.Cha, cha))
                _effectTargets[id] = state = new EffectTarget { Cha = cha };
            return state;
        }

        private void ResolveSkinEffects()
        {
            if (_skinEffectSearched && _skinEffectType != null) return;
            _skinEffectSearched = true;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type found = assembly.GetType("KK_SkinEffects.SkinEffectsController", false);
                if (found == null) continue;
                _skinEffectType = found;
                _textureLoaderType = assembly.GetType("KK_SkinEffects.TextureLoader", false);
                break;
            }
        }

        private object ModController(ChaControl cha)
        {
            if (cha == null) return null;
            ResolveSkinEffects();
            return _skinEffectType == null ? null : cha.GetComponent(_skinEffectType);
        }

        private int ChannelMax(int channel)
        {
            if (channel == Cheek) return 100;
            if (channel == GameTears || IsLiquidChannel(channel)) return 3;
            ResolveSkinEffects();
            PropertyInfo count = _textureLoaderType?.GetProperty(TextureCounts[channel],
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            try
            {
                int available = Convert.ToInt32(count?.GetValue(null, null) ?? 0);
                return available > 0 ? available : 3;
            }
            catch { return 3; }
        }

        private ChannelCycleSettings ChannelSettings(AutoCycleSettings a, int channel)
        {
            switch (channel)
            {
                case Cheek: return a.HohoAka;
                case GameTears: return a.Tears;
                case FaceLiquid: return a.FaceSiru;
                case ModTears: return a.ModTears;
                case ModDrool: return a.ModDrool;
                case ModSaliva: return a.ModSaliva;
                case ModSweat: return a.ModSweat;
                case FrontUpLiquid: return a.FrontUpSiru;
                case FrontDownLiquid: return a.FrontDownSiru;
                case BackUpLiquid: return a.BackUpSiru;
                default: return a.BackDownSiru;
            }
        }

        private int ReadEffect(ChaControl cha, int channel)
        {
            if (cha == null) return 0;
            if (channel == Cheek) return Mathf.RoundToInt(cha.fileStatus.hohoAkaRate * 100f);
            if (channel == GameTears) return cha.tearsLv;
            if (IsLiquidChannel(channel))
            {
                byte[] levels = cha.fileStatus.siruLv;
                int index = (int)LiquidPart(channel);
                return levels != null && index < levels.Length ? levels[index] : 0;
            }
            object controller = ModController(cha);
            PropertyInfo property = _skinEffectType?.GetProperty(ModProperties[channel]);
            try { return controller != null && property != null ? Convert.ToInt32(property.GetValue(controller, null)) : 0; }
            catch { return 0; }
        }

        private void WriteEffect(ChaControl cha, int channel, int value)
        {
            if (cha == null || cha.fileStatus == null) return;
            value = Mathf.Clamp(value, 0, ChannelMax(channel));
            if (channel == Cheek) { cha.ChangeHohoAkaRate(value / 100f); return; }
            if (channel == GameTears) { ApplyTearsLevel(cha, value); return; }
            if (IsLiquidChannel(channel))
            {
                cha.SetSiruFlags(LiquidPart(channel), (byte)value);
                UpdateSiru?.Invoke(cha, new object[] { true });
                return;
            }
            object controller = ModController(cha);
            PropertyInfo property = _skinEffectType?.GetProperty(ModProperties[channel]);
            if (controller == null || property == null || !property.CanWrite) return;
            property.SetValue(controller, value, null); // The MOD setter updates its own texture.
        }

        private void SetEffect(int channel, int value, bool manual = true)
        {
            ChaControl cha = SelectedEffectCharacter();
            if (cha == null) return;
            try
            {
                value = Mathf.Clamp(value, 0, ChannelMax(channel));
                if (manual) { StateFor(cha).ManualOverride[channel]=true; ChannelSettings(Auto, channel).Enabled = false; SaveSettings(); }
                StateFor(cha).Desired[channel] = value;
                WriteEffect(cha, channel, value);
            }
            catch (Exception ex) { LogWarn("effect apply failed: " + ex.Message); }
        }

        private void ReleaseEffect(int channel)
        {
            ChaControl cha = SelectedEffectCharacter();
            if (cha != null) StateFor(cha).Desired[channel] = null;
        }

        private void ReadEffectSettings(ChaControl cha)
        {
            AutoCycleSettings a = Auto;
            if (a == null || cha == null) return;
            for (int channel = 0; channel < EffectChannelCount; channel++)
                ChannelSettings(a, channel).ManualValue = ReadEffect(cha, channel);
        }

        private void DrawEffectControls(AutoCycleSettings a)
        {
            ChaControl selected = SelectedEffectCharacter();
            GUILayout.BeginHorizontal();
            GUILayout.Label("効果の対象", GUILayout.Width(85f));
            foreach (ChaControl cha in _effectFemales)
            {
                if (cha == null) continue;
                int id = cha.GetInstanceID();
                string targetLabel = cha == _effectIndependentActor ? "呼出キャラ" : cha.name;
                if (GUILayout.Toggle(id == _selectedEffectCharacter, targetLabel, GUI.skin.button, GUILayout.Width(105f))
                    && id != _selectedEffectCharacter)
                {
                    _selectedEffectCharacter = id;
                    selected = cha;
                    ReadEffectSettings(cha);
                }
            }
            if (GUILayout.Button("現在値を読む", GUILayout.Width(100f))) ReadEffectSettings(selected);
            GUILayout.EndHorizontal();
            if (selected == null) { GUILayout.Label("効果の対象を選択してください"); return; }
            string[] labels = { "頬赤", "涙(標準)", "顔の汁(標準)", "涙(MOD)", "よだれ(Drool)", "唾液(Saliva)", "汗(MOD)", "精液: 胸・腹上", "精液: 腹下", "精液: 背中", "精液: 腰・尻" };
            for (int channel = 0; channel < labels.Length; channel++)
            {
                int c = channel;
                bool available = (c < ModTears || IsLiquidChannel(c)) || ModController(selected) != null;
                bool enabled = GUI.enabled;
                GUI.enabled = enabled && available;
                ChannelSettings(a, c).ManualValue = ReadEffect(selected, c);
                if (StateFor(selected).ManualOverride[c]) ChannelSettings(a,c).Enabled=false;
                DrawValueChannelRow(labels[c], ChannelSettings(a, c), 0, ChannelMax(c), c == Cheek ? "%" : "",
                    v => SetEffect(c, v), () => ReleaseEffect(c));
                if (ChannelSettings(a,c).Enabled) StateFor(selected).ManualOverride[c]=false;
                GUI.enabled = enabled;
            }
            if (ModController(selected) == null)
                GUILayout.Label("Additional Skin Effects が未導入、または対象キャラにControllerがありません");
        }

        private void TickEffectCycle()
        {
            AutoCycleSettings a = Auto;
            if (a == null || !a.Enabled || !IsInHScene()) return;
            ChaControl cha = SelectedEffectCharacter();
            if (cha == null || !cha.gameObject.activeInHierarchy) return;
            float now = Time.unscaledTime;
            for (int channel = 0; channel < EffectChannelCount; channel++)
            {
                ChannelCycleSettings settings = ChannelSettings(a, channel);
                CycleClock clock = _effectClocks[channel];
                if (StateFor(cha).ManualOverride[channel] || !settings.Enabled || now < clock.NextAt || (!IsLiquidChannel(channel) && channel >= ModTears && ModController(cha) == null)) continue;
                List<int> steps = BuildValueSteps(settings);
                if (steps.Count == 0) continue;
                clock.Index = NextIndex(clock.Index, steps.Count, a.EffectiveOrder(settings.Order));
                SetEffect(channel, steps[clock.Index], false);
                clock.NextAt = now + a.EffectiveHold(settings.HoldSeconds);
            }
        }

        private void MaintainEffectChannels()
        {
            RefreshEffectScene();
            if (!IsInHScene()) return;
            foreach (EffectTarget target in _effectTargets.Values)
            {
                if (target.Cha == null || !target.Cha.gameObject.activeInHierarchy) continue;
                for (int channel = 0; channel < EffectChannelCount; channel++)
                {
                    int? desired = target.Desired[channel];
                    if (!desired.HasValue) continue;
                    try { if (ReadEffect(target.Cha, channel) != desired.Value) WriteEffect(target.Cha, channel, desired.Value); }
                    catch (Exception ex) { LogWarn("effect maintain failed: " + ex.Message); }
                }
            }
        }
    }
}
