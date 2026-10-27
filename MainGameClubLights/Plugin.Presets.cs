using UnityEngine;

namespace MainGameClubLights
{
    public sealed partial class Plugin
    {
        // ── プリセット適用 ───────────────────────────────────────────────────

        internal void ApplyPresetToLight(
            LightInstanceSettings li,
            string presetId,
            bool fromBeatSync = false,
            string reason = "manual")
        {
            if (string.IsNullOrEmpty(presetId)) return;

            LightPreset preset = FindPreset(presetId);
            if (preset == null || preset.Settings == null) return;

            var src = preset.Settings;
            bool keepSpotByUser = fromBeatSync && li.SpotAnglePinnedByUser;
            float beforeSpot = li.SpotAngle;

            if (src.Rainbow == null) src.Rainbow = new RainbowSettings();
            if (src.Strobe == null) src.Strobe = new StrobeSettings();

            if (!fromBeatSync && li.SpotAnglePinnedByUser)
                li.SpotAnglePinnedByUser = false;

            // 保持する値を退避
            string savedId   = li.Id;
            string savedName = li.Name;

            // 全フィールドコピー
            li.Enabled      = src.Enabled;
            li.FollowCamera = src.FollowCamera;
            li.OffsetX      = src.OffsetX;
            li.OffsetY      = src.OffsetY;
            li.OffsetZ      = src.OffsetZ;
            li.WorldPosX    = src.WorldPosX;
            li.WorldPosY    = src.WorldPosY;
            li.WorldPosZ    = src.WorldPosZ;
            li.Intensity      = src.Intensity;
            li.Range          = src.Range;
            if (!keepSpotByUser)
            {
                li.SpotAngle      = src.SpotAngle;
                li.InnerSpotAngle = src.InnerSpotAngle;
            }
            li.ColorR       = src.ColorR;
            li.ColorG       = src.ColorG;
            li.ColorB       = src.ColorB;
            li.ShowMarker   = src.ShowMarker;
            li.ShowArrow    = src.ShowArrow;
            li.ShowGizmo    = src.ShowGizmo;
            li.MarkerSize   = src.MarkerSize;
            li.ArrowScale   = src.ArrowScale;
            li.GizmoSize    = src.GizmoSize;
            li.RotX         = src.RotX;
            li.RotY         = src.RotY;
            li.RotZ         = src.RotZ;
            li.LookAtFemale  = src.LookAtFemale;
            li.LookAtOffsetX = src.LookAtOffsetX;
            li.LookAtOffsetY = src.LookAtOffsetY;
            li.LookAtOffsetZ = src.LookAtOffsetZ;
            li.RevolutionEnabled  = src.RevolutionEnabled;
            li.RevolutionRadius   = src.RevolutionRadius;
            li.RevolutionSpeed    = src.RevolutionSpeed;
            li.RevolutionAngleDeg = 0f;
            li.RevolutionCenterX  = src.RevolutionCenterX;
            li.RevolutionCenterY  = src.RevolutionCenterY;
            li.RevolutionCenterZ  = src.RevolutionCenterZ;
            li.RotationEnabled    = src.RotationEnabled;
            li.RotationSpeed      = src.RotationSpeed;
            li.RotationAngleDeg   = 0f;

            li.Rainbow.Enabled    = src.Rainbow.Enabled;
            li.Rainbow.CycleSpeed = src.Rainbow.CycleSpeed;
            li.Strobe.Enabled     = src.Strobe.Enabled;
            li.Strobe.FrequencyHz = src.Strobe.FrequencyHz;
            li.Strobe.DutyRatio   = src.Strobe.DutyRatio;
            li.Beat               = CloneBeatAssignment(src.Beat);
            li.IntensityLoop      = CloneLoopSettings(src.IntensityLoop, 0.5f, 1.0f, 0.3f);
            li.RangeLoop          = CloneLoopSettings(src.RangeLoop, 1f, 10f, 0.5f);
            li.SpotAngleLoop      = CloneLoopSettings(src.SpotAngleLoop, 10f, 60f, 0.5f);

            if (li.Mirrorball == null) li.Mirrorball = new MirrorballCookieSettings();
            if (src.Mirrorball == null) src.Mirrorball = new MirrorballCookieSettings();
            li.Mirrorball.Enabled    = src.Mirrorball.Enabled;
            li.Mirrorball.Resolution = src.Mirrorball.Resolution;
            li.Mirrorball.DotCount   = src.Mirrorball.DotCount;
            li.Mirrorball.DotSize    = src.Mirrorball.DotSize;
            li.Mirrorball.Scatter    = src.Mirrorball.Scatter;
            li.Mirrorball.Softness   = src.Mirrorball.Softness;
            li.Mirrorball.Animate    = src.Mirrorball.Animate;
            li.Mirrorball.SpinSpeed  = src.Mirrorball.SpinSpeed;
            li.Mirrorball.UpdateHz   = src.Mirrorball.UpdateHz;
            li.Mirrorball.Twinkle    = src.Mirrorball.Twinkle;

            // Rainbow が有効なら Hue を初期化
            li.RainbowHue = src.Rainbow.Enabled
                ? RGBToHue(src.ColorR, src.ColorG, src.ColorB)
                : 0f;

            // 退避値を復元
            li.Id   = savedId;
            li.Name = savedName;

            // ライトオブジェクトに即時反映
            var entry = FindEntry(li);
            if (entry?.Light != null)
            {
                entry.Light.intensity = li.Intensity;
                entry.Light.spotAngle = li.SpotAngle;
                entry.Light.color     = new Color(li.ColorR, li.ColorG, li.ColorB);
            }

            _log.Info(
                $"[PresetApply] source={(fromBeatSync ? "beat" : "direct")} reason={reason} " +
                $"light={li.Id} preset={presetId} keepSpotByUser={keepSpotByUser} " +
                $"spotBefore={beforeSpot:F1} spotAfter={li.SpotAngle:F1}");
        }

        internal void ApplyPresetToNativeLight(string presetId)
        {
            if (string.IsNullOrEmpty(presetId)) return;
            LightPreset preset = FindPreset(presetId);
            if (preset == null || preset.Settings == null) return;

            foreach (var nl in _nativeLights)
            {
                if (nl == null) continue;
                nl.intensity = preset.Settings.Intensity * _settings.NativeLight.IntensityScale;
                nl.color     = new Color(preset.Settings.ColorR, preset.Settings.ColorG, preset.Settings.ColorB);
            }
        }

        // ── プリセット保存（現在のライト設定から） ───────────────────────────

        internal LightPreset SavePresetFromLight(LightInstanceSettings li, string name)
        {
            name = (name ?? "").Trim();
            if (string.IsNullOrEmpty(name))
                name = $"Preset {_settings.Presets.Count + 1}";

            var src = li;
            var settings = new LightInstanceSettings
            {
                Enabled      = src.Enabled,
                FollowCamera = src.FollowCamera,
                OffsetX      = src.OffsetX,
                OffsetY      = src.OffsetY,
                OffsetZ      = src.OffsetZ,
                WorldPosX    = src.WorldPosX,
                WorldPosY    = src.WorldPosY,
                WorldPosZ    = src.WorldPosZ,
                Intensity      = src.Intensity,
                Range          = src.Range,
                SpotAngle      = src.SpotAngle,
                InnerSpotAngle = src.InnerSpotAngle,
                ColorR       = src.ColorR,
                ColorG       = src.ColorG,
                ColorB       = src.ColorB,
                ShowMarker   = src.ShowMarker,
                ShowArrow    = src.ShowArrow,
                ShowGizmo    = src.ShowGizmo,
                MarkerSize   = src.MarkerSize,
                ArrowScale   = src.ArrowScale,
                GizmoSize    = src.GizmoSize,
                RotX         = src.RotX,
                RotY         = src.RotY,
                RotZ         = src.RotZ,
                LookAtFemale  = src.LookAtFemale,
                LookAtOffsetX = src.LookAtOffsetX,
                LookAtOffsetY = src.LookAtOffsetY,
                LookAtOffsetZ = src.LookAtOffsetZ,
                RevolutionEnabled = src.RevolutionEnabled,
                RevolutionRadius  = src.RevolutionRadius,
                RevolutionSpeed   = src.RevolutionSpeed,
                RevolutionCenterX = src.RevolutionCenterX,
                RevolutionCenterY = src.RevolutionCenterY,
                RevolutionCenterZ = src.RevolutionCenterZ,
                RotationEnabled   = src.RotationEnabled,
                RotationSpeed     = src.RotationSpeed,
                Beat = CloneBeatAssignment(src.Beat),
                IntensityLoop = CloneLoopSettings(src.IntensityLoop, 0.5f, 1.0f, 0.3f),
                RangeLoop     = CloneLoopSettings(src.RangeLoop, 1f, 10f, 0.5f),
                SpotAngleLoop = CloneLoopSettings(src.SpotAngleLoop, 10f, 60f, 0.5f),
                Rainbow = new RainbowSettings
                {
                    Enabled    = src.Rainbow.Enabled,
                    CycleSpeed = src.Rainbow.CycleSpeed
                },
                Strobe = new StrobeSettings
                {
                    Enabled     = src.Strobe.Enabled,
                    FrequencyHz = src.Strobe.FrequencyHz,
                    DutyRatio   = src.Strobe.DutyRatio
                },
                Mirrorball = new MirrorballCookieSettings
                {
                    Enabled    = src.Mirrorball != null && src.Mirrorball.Enabled,
                    Resolution = src.Mirrorball != null ? src.Mirrorball.Resolution : 256,
                    DotCount   = src.Mirrorball != null ? src.Mirrorball.DotCount : 220,
                    DotSize    = src.Mirrorball != null ? src.Mirrorball.DotSize : 0.03f,
                    Scatter    = src.Mirrorball != null ? src.Mirrorball.Scatter : 0.65f,
                    Softness   = src.Mirrorball != null ? src.Mirrorball.Softness : 0.45f,
                    Animate    = src.Mirrorball == null || src.Mirrorball.Animate,
                    SpinSpeed  = src.Mirrorball != null ? src.Mirrorball.SpinSpeed : 0.12f,
                    UpdateHz   = src.Mirrorball != null ? src.Mirrorball.UpdateHz : 8f,
                    Twinkle    = src.Mirrorball != null ? src.Mirrorball.Twinkle : 0.20f
                }
                // Id/Name はプリセットに持たせない（ライト固有）
            };

            var preset = new LightPreset
            {
                Id       = GenerateId(),
                Name     = name,
                Settings = settings
            };
            _settings.Presets.Add(preset);
            _log.Info($"[Preset] 保存 id={preset.Id} name={name}");
            SaveSettingsNow("preset-save");
            return preset;
        }

        internal void DeletePreset(int index)
        {
            if (index < 0 || index >= _settings.Presets.Count) return;
            _log.Info($"[Preset] 削除 id={_settings.Presets[index].Id}");
            _settings.Presets.RemoveAt(index);
            SaveSettingsNow("preset-delete");
        }

        internal LightPreset FindPreset(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var p in _settings.Presets)
                if (p.Id == id) return p;
            return null;
        }

        private static BeatPresetAssignment CloneBeatAssignment(BeatPresetAssignment src)
        {
            src = src ?? new BeatPresetAssignment();
            return new BeatPresetAssignment
            {
                LowPresetId  = src.LowPresetId ?? "",
                MidPresetId  = src.MidPresetId ?? "",
                HighPresetId = src.HighPresetId ?? ""
            };
        }

        private static LoopSettings CloneLoopSettings(LoopSettings src, float minDefault, float maxDefault, float speedDefault)
        {
            src = src ?? new LoopSettings
            {
                MinValue = minDefault,
                MaxValue = maxDefault,
                SpeedHz = speedDefault
            };
            return new LoopSettings
            {
                Enabled   = src.Enabled,
                VideoLink = src.VideoLink,
                MinValue  = src.MinValue,
                MaxValue  = src.MaxValue,
                SpeedHz   = src.SpeedHz
            };
        }
    }
}
