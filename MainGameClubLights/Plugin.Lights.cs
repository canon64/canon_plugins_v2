using System.Collections.Generic;
using System.Reflection;
using MainGameTransformGizmo;
using UnityEngine;

namespace MainGameClubLights
{
    public sealed partial class Plugin
    {
        internal sealed class LightEntry
        {
            public LightInstanceSettings Settings;
            public GameObject            Go;
            public Light                 Light;
            public TransformGizmo        Gizmo;
            public float                 NextGizmoAttachRetryAt;
            public Vector3               LastDesiredPos;
            public GameObject            Marker;
            public Renderer              MarkerRenderer;
            public Material              MarkerMaterial;
            public GameObject            Arrow;
            public Renderer              ArrowRenderer;
            public Renderer              ArrowTipRenderer;
            public Material              ArrowMaterial;
            public Material              ArrowTipMaterial;
            public bool                  VrGrabbed;
            public Vector3               VrGrabOffset;
            public float                 IntensityLoopPhase;
            public float                 RangeLoopPhase;
            public float                 SpotAngleLoopPhase;
            public RenderTexture         MirrorballCookieTexture;
            public Texture2D             MirrorballDotTexture;
            public float                 MirrorballCookieNextRebuildAt;
            public int                   MirrorballCookieConfigHash;
            public float                 MirrorballCookieSpinDeg;
            public int                   MirrorballDotSoftnessQuant;
        }

        private readonly List<LightEntry> _lightEntries = new List<LightEntry>();
        private Material _mirrorballDotMaterial;
        private static readonly FieldInfo FemaleListField = HarmonyLib.AccessTools.Field(typeof(HSceneProc), "lstFemale");
        private int _femalePositionFrame = -1;
        private Vector3 _femalePositionThisFrame;
        private ChaControl _femalePositionChara;
        private Transform _femaleHeadRoot;
        private Transform _femaleHeadBone;

        // ── 生成 / 破棄 ──────────────────────────────────────────────────────

        private void BuildLightObjects()
        {
            EnsureFolders();
            foreach (var li in _settings.Lights)
                CreateLightEntry(li);
        }

        private LightEntry CreateLightEntry(LightInstanceSettings li)
        {
            var go = new GameObject($"ClubLight_{li.Id}");
            var light = go.AddComponent<Light>();
            light.type           = li.SpotAngle > 179f ? LightType.Point : LightType.Spot;
            light.intensity      = li.Intensity;
            light.range          = li.Range;
            light.spotAngle      = Mathf.Clamp(li.SpotAngle, 1f, 179f);
            light.innerSpotAngle = Mathf.Clamp(li.InnerSpotAngle, 0f, Mathf.Clamp(li.SpotAngle, 1f, 179f));
            light.color          = new Color(li.ColorR, li.ColorG, li.ColorB);
            light.enabled     = li.Enabled;
            light.renderMode  = LightRenderMode.ForcePixel;
            li.RainbowHue     = RGBToHue(li.ColorR, li.ColorG, li.ColorB);

            var entry = new LightEntry { Settings = li, Go = go, Light = light };

            // 球マーカー
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.transform.SetParent(go.transform, worldPositionStays: false);
            marker.transform.localPosition = Vector3.zero;
            marker.transform.localScale    = Vector3.one * li.MarkerSize;
            // コライダー不要
            var col = marker.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var mr = marker.GetComponent<Renderer>();
            if (mr != null)
            {
                entry.MarkerMaterial = new Material(Shader.Find("Unlit/Color"));
                entry.MarkerMaterial.color = new Color(li.ColorR, li.ColorG, li.ColorB);
                mr.material = entry.MarkerMaterial;
            }
            marker.layer = 0;
            marker.SetActive(li.ShowMarker);
            entry.Marker         = marker;
            entry.MarkerRenderer = mr;

            // 方向インジケーター（スティック＋先端球）
            var arrowRoot = new GameObject("Arrow");
            arrowRoot.transform.SetParent(go.transform, worldPositionStays: false);

            var stick = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stick.transform.SetParent(arrowRoot.transform, worldPositionStays: false);
            stick.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            stick.transform.localPosition = new Vector3(0f, 0f, 0.12f);
            stick.transform.localScale    = new Vector3(0.015f, 0.12f, 0.015f);
            var stickCol = stick.GetComponent<Collider>(); if (stickCol != null) Destroy(stickCol);
            var stickMat = new Material(Shader.Find("Unlit/Color"));
            stickMat.color = new Color(li.ColorR, li.ColorG, li.ColorB);
            stick.GetComponent<Renderer>().material = stickMat;
            entry.ArrowMaterial = stickMat;
            stick.layer = 0;

            // コーン先端（スティック先端 z=0.18 から突き出す）
            var tip = new GameObject("ArrowTip");
            tip.transform.SetParent(arrowRoot.transform, worldPositionStays: false);
            tip.transform.localPosition = new Vector3(0f, 0f, 0.18f);
            tip.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // Y→Z でコーンをZ+方向に向ける
            var tipMf = tip.AddComponent<MeshFilter>();
            tipMf.mesh = BuildConeMesh(0.04f, 0.12f, 12);
            var tipMr = tip.AddComponent<MeshRenderer>();
            var tipMat = new Material(Shader.Find("Unlit/Color"));
            tipMat.color = new Color(li.ColorR, li.ColorG, li.ColorB);
            tipMr.material = tipMat;
            entry.ArrowTipMaterial = tipMat;
            tip.layer = 0;

            arrowRoot.SetActive(li.ShowArrow);
            entry.Arrow            = arrowRoot;
            entry.ArrowRenderer    = stick.GetComponent<Renderer>();
            entry.ArrowTipRenderer = tipMr;

            // 所属フォルダがあればその子に（位置=フォルダ基準ローカル）。未所属はワールド座標で単体。
            Transform folderParent = FindFolderGo(li.FolderId);
            if (folderParent != null)
                go.transform.SetParent(folderParent, worldPositionStays: false);

            if (!li.FollowCamera)
            {
                if (folderParent != null)
                    go.transform.localPosition = new Vector3(li.WorldPosX, li.WorldPosY, li.WorldPosZ);
                else
                    go.transform.position = new Vector3(li.WorldPosX, li.WorldPosY, li.WorldPosZ);
                TryAttachGizmo(entry);
            }
            else
            {
                go.transform.position = GetReferencePosition()
                    + new Vector3(li.OffsetX, li.OffsetY, li.OffsetZ);
            }

            UpdateMirrorballCookie(entry, 0f, Time.unscaledTime);

            _lightEntries.Add(entry);
            var cam = Camera.main;
            string camPosStr    = cam != null ? cam.transform.position.ToString() : "null";
            string cullingStr   = cam != null ? cam.cullingMask.ToString() : "null";
            bool   layer0vis    = cam != null && (cam.cullingMask & 1) != 0;
            _log.Info($"[Lights] 生成 id={li.Id} name={li.Name} goPos={go.transform.position} " +
                $"followCam={li.FollowCamera} showMarker={li.ShowMarker} " +
                $"markerNull={entry.Marker == null} markerActive={entry.Marker?.activeSelf} " +
                $"mrNull={entry.MarkerRenderer == null} camPos={camPosStr} " +
                $"cullingMask={cullingStr} layer0visible={layer0vis} " +
                $"gizmoAvail={TransformGizmoApi.IsAvailable} gizmoNull={entry.Gizmo == null}");
            return entry;
        }

        private void DestroyLightEntry(LightEntry entry)
        {
            ReleaseMirrorballCookie(entry);
            if (entry.Gizmo != null)
                Destroy(entry.Gizmo);
            if (entry.Go != null)
                Destroy(entry.Go);
            if (entry.MarkerMaterial != null) Destroy(entry.MarkerMaterial);
            if (entry.ArrowMaterial != null) Destroy(entry.ArrowMaterial);
            if (entry.ArrowTipMaterial != null) Destroy(entry.ArrowTipMaterial);
        }

        private void DestroyAllLights()
        {
            StopVRLightGrab();
            foreach (var e in _lightEntries)
                DestroyLightEntry(e);
            _lightEntries.Clear();
            ReleaseMirrorballSharedResources();
            DestroyFolders();
        }

        // ── 毎フレーム更新 ───────────────────────────────────────────────────

        private void UpdateLights()
        {
            float dt  = Time.unscaledDeltaTime;
            float now = Time.unscaledTime;
            bool needsBeatValues = false;
            foreach (var entry in _lightEntries)
            {
                var settings = entry.Settings;
                if (settings != null && settings.Enabled &&
                    (settings.RangeLoop.Enabled || settings.SpotAngleLoop.Enabled ||
                     settings.Rainbow.Enabled || settings.IntensityLoop.Enabled || settings.Strobe.Enabled))
                {
                    needsBeatValues = true;
                    break;
                }
            }
            float beatLoopHz = 0f;
            float zoneSpeed01 = 0f;
            bool hasBeatLoopHz = needsBeatValues && TryGetBeatLinkedLoopHz(out beatLoopHz);
            bool hasZoneSpeed  = needsBeatValues && TryGetBeatZoneSpeed01(out zoneSpeed01);

            foreach (var entry in _lightEntries)
            {
                var li    = entry.Settings;
                var light = entry.Light;

                if (!li.Enabled)
                {
                    if (light.enabled) light.enabled = false;
                    if (entry.Marker != null && entry.Marker.activeSelf) entry.Marker.SetActive(false);
                    if (entry.Arrow  != null && entry.Arrow.activeSelf) entry.Arrow.SetActive(false);
                    if (entry.Gizmo  != null) entry.Gizmo.SetVisible(false);
                    continue;
                }
                if (!light.enabled) light.enabled = true;

                // 位置・回転更新
                if (li.FollowCamera)
                    UpdateFollowCameraPosition(entry, dt);
                else
                {
                    // 公転中はギズモ不要。停止時は再アタッチ
                    if (li.RevolutionEnabled)
                    {
                        if (entry.Gizmo != null) TryDetachGizmo(entry);
                        li.RevolutionAngleDeg = (li.RevolutionAngleDeg + li.RevolutionSpeed * dt) % 360f;
                        float rev = li.RevolutionAngleDeg * Mathf.Deg2Rad;
                        Vector3 orbit = new Vector3(
                            li.RevolutionCenterX + Mathf.Sin(rev) * li.RevolutionRadius,
                            li.RevolutionCenterY,
                            li.RevolutionCenterZ + Mathf.Cos(rev) * li.RevolutionRadius);
                        // フォルダ所属なら中心・軌道をフォルダ基準ローカルで扱う（公転ごとフォルダ追従）
                        bool inFolderRev = !string.IsNullOrEmpty(li.FolderId) && entry.Go.transform.parent != null;
                        if (inFolderRev) entry.Go.transform.localPosition = orbit;
                        else             entry.Go.transform.position      = orbit;
                    }
                    else
                    {
                        if (entry.Gizmo == null && now >= entry.NextGizmoAttachRetryAt)
                        {
                            TryAttachGizmo(entry);
                            if (entry.Gizmo == null)
                                entry.NextGizmoAttachRetryAt = now + 1f;
                        }
                    }

                    // 自転 > LookAtFemale > 通常 の優先順
                    if (li.RotationEnabled)
                    {
                        li.RotationAngleDeg = (li.RotationAngleDeg + li.RotationSpeed * dt) % 360f;
                        entry.Go.transform.rotation = Quaternion.Euler(li.RotX, li.RotationAngleDeg, li.RotZ);
                    }
                    else if (li.LookAtFemale)
                    {
                        Vector3 femalePos = GetFemalePosition();
                        if (femalePos != Vector3.zero)
                        {
                            entry.Go.transform.LookAt(femalePos);
                            if (li.LookAtOffsetX != 0f || li.LookAtOffsetY != 0f || li.LookAtOffsetZ != 0f)
                                entry.Go.transform.rotation *= Quaternion.Euler(li.LookAtOffsetX, li.LookAtOffsetY, li.LookAtOffsetZ);
                        }
                    }
                    else
                    {
                        // フォルダ基準のローカル回転（フォルダを回すと一緒に回る）
                        Quaternion rotation = Quaternion.Euler(li.RotX, li.RotY, li.RotZ);
                        if (entry.Go.transform.localRotation != rotation)
                            entry.Go.transform.localRotation = rotation;
                    }
                }

                if (li.SpotAnglePinnedByUser && li.SpotAngleLoop.Enabled)
                {
                    li.SpotAngleLoop.Enabled = false;
                    _log.Info($"[SpotAngle] auto-disable-loop id={li.Id} reason=pin-by-user");
                }

                // スポット角・範囲（ループ有効時はサイン波で上書き）
                float loopRange = li.Range;
                if (li.RangeLoop.Enabled)
                {
                    if (li.RangeLoop.VideoLink)
                    {
                        if (hasBeatLoopHz && beatLoopHz > 0f)
                            entry.RangeLoopPhase += beatLoopHz * dt;
                        float lt = (Mathf.Sin(entry.RangeLoopPhase * Mathf.PI * 2f) + 1f) * 0.5f;
                        loopRange = Mathf.Lerp(li.RangeLoop.MinValue, li.RangeLoop.MaxValue, lt);
                    }
                    else if (li.RangeLoop.SpeedHz > 0f)
                    {
                        entry.RangeLoopPhase += li.RangeLoop.SpeedHz * dt;
                        float lt = (Mathf.Sin(entry.RangeLoopPhase * Mathf.PI * 2f) + 1f) * 0.5f;
                        loopRange = Mathf.Lerp(li.RangeLoop.MinValue, li.RangeLoop.MaxValue, lt);
                    }
                }
                if (light.range != loopRange) light.range = loopRange;

                float loopSpotAngle = li.SpotAngle;
                if (li.SpotAngleLoop.Enabled)
                {
                    if (li.SpotAngleLoop.VideoLink)
                    {
                        if (hasBeatLoopHz && beatLoopHz > 0f)
                            entry.SpotAngleLoopPhase += beatLoopHz * dt;
                        float lt = (Mathf.Sin(entry.SpotAngleLoopPhase * Mathf.PI * 2f) + 1f) * 0.5f;
                        loopSpotAngle = Mathf.Lerp(li.SpotAngleLoop.MinValue, li.SpotAngleLoop.MaxValue, lt);
                    }
                    else if (li.SpotAngleLoop.SpeedHz > 0f)
                    {
                        entry.SpotAngleLoopPhase += li.SpotAngleLoop.SpeedHz * dt;
                        float lt = (Mathf.Sin(entry.SpotAngleLoopPhase * Mathf.PI * 2f) + 1f) * 0.5f;
                        loopSpotAngle = Mathf.Lerp(li.SpotAngleLoop.MinValue, li.SpotAngleLoop.MaxValue, lt);
                    }
                }
                if (loopSpotAngle > 179f)
                {
                    if (light.type != LightType.Point)
                        light.type = LightType.Point;
                    if (light.spotAngle != 179f) light.spotAngle = 179f;
                    if (light.innerSpotAngle != 0f) light.innerSpotAngle = 0f;
                }
                else
                {
                    if (light.type != LightType.Spot)
                        light.type = LightType.Spot;
                    if (light.spotAngle != loopSpotAngle) light.spotAngle = loopSpotAngle;
                    float appliedInner = Mathf.Clamp(li.InnerSpotAngle, 0f, loopSpotAngle);
                    if (loopSpotAngle <= 5f && appliedInner < loopSpotAngle * 0.98f)
                        appliedInner = loopSpotAngle; // 低角度時は硬いエッジで点に寄せる
                    if (light.innerSpotAngle != appliedInner) light.innerSpotAngle = appliedInner;
                }

                // 色（レインボー）
                float r = li.ColorR, g = li.ColorG, b = li.ColorB;
                if (li.Rainbow.Enabled)
                {
                    float cycleSpeed = ResolveRainbowCycleSpeed(li.Rainbow, hasBeatLoopHz, beatLoopHz, hasZoneSpeed, zoneSpeed01);
                    li.RainbowHue = (li.RainbowHue + cycleSpeed * dt) % 1f;
                    Color rainbowColor = Color.HSVToRGB(li.RainbowHue, 1f, 1f);
                    r = rainbowColor.r;
                    g = rainbowColor.g;
                    b = rainbowColor.b;
                }
                Color targetColor = new Color(r, g, b);
                if (entry.Gizmo != null) entry.Gizmo.SetCenterColor(targetColor);
                if (li.Rainbow.Enabled || light.color != targetColor) light.color = targetColor;

                // 強度（ループ → ストロボの順で適用）
                float intensity = li.Intensity;
                if (li.IntensityLoop.Enabled)
                {
                    if (li.IntensityLoop.BeatFollow)
                    {
                        float s = hasZoneSpeed ? zoneSpeed01 : 0f;
                        intensity = Mathf.Lerp(li.IntensityLoop.MinValue, li.IntensityLoop.MaxValue, s);
                    }
                    else if (li.IntensityLoop.VideoLink)
                    {
                        if (hasBeatLoopHz && beatLoopHz > 0f)
                            entry.IntensityLoopPhase += beatLoopHz * dt;
                        float lt = (Mathf.Sin(entry.IntensityLoopPhase * Mathf.PI * 2f) + 1f) * 0.5f;
                        intensity = Mathf.Lerp(li.IntensityLoop.MinValue, li.IntensityLoop.MaxValue, lt);
                    }
                    else if (li.IntensityLoop.SpeedHz > 0f)
                    {
                        entry.IntensityLoopPhase += li.IntensityLoop.SpeedHz * dt;
                        float lt = (Mathf.Sin(entry.IntensityLoopPhase * Mathf.PI * 2f) + 1f) * 0.5f;
                        intensity = Mathf.Lerp(li.IntensityLoop.MinValue, li.IntensityLoop.MaxValue, lt);
                    }
                }
                if (li.Strobe.Enabled)
                {
                    float freq = ResolveStrobeFrequency(li.Strobe, hasBeatLoopHz, beatLoopHz, hasZoneSpeed, zoneSpeed01);
                    if (freq > 0f)
                    {
                        float phase = (Time.unscaledTime * freq) % 1f;
                        float duty = ResolveStrobeDutyRatio(li.Strobe, hasBeatLoopHz, beatLoopHz, hasZoneSpeed, zoneSpeed01, Time.unscaledTime);
                        intensity = phase < duty ? intensity : 0f;
                    }
                }
                if (li.IntensityLoop.Enabled || li.Strobe.Enabled || light.intensity != intensity)
                    light.intensity = intensity;

                UpdateMirrorballCookie(entry, dt, now);

                // マーカー・方向インジケーター更新
                if (entry.Marker != null)
                {
                    if (entry.Marker.activeSelf != li.ShowMarker) entry.Marker.SetActive(li.ShowMarker);
                    if (li.ShowMarker)
                    {
                        Vector3 markerScale = Vector3.one * li.MarkerSize;
                        if (entry.Marker.transform.localScale != markerScale)
                            entry.Marker.transform.localScale = markerScale;
                        if (entry.MarkerMaterial != null)
                        {
                            var material = entry.MarkerMaterial;
                            if (material != null && material.color != targetColor)
                                material.color = targetColor;
                        }
                    }
                }
                if (entry.Arrow != null)
                {
                    if (entry.Arrow.activeSelf != li.ShowArrow) entry.Arrow.SetActive(li.ShowArrow);
                    if (li.ShowArrow)
                    {
                        Vector3 arrowScale = Vector3.one * Mathf.Max(0.01f, li.ArrowScale);
                        if (entry.Arrow.transform.localScale != arrowScale)
                            entry.Arrow.transform.localScale = arrowScale;
                        // カメラ追従時のみ camera.forward を表示（自由配置時は go の向きを継承）
                        if (li.FollowCamera)
                        {
                            var cam = Camera.main;
                            if (cam != null)
                                entry.Arrow.transform.rotation = Quaternion.LookRotation(cam.transform.forward);
                        }
                        if (entry.ArrowMaterial != null)
                        {
                            var material = entry.ArrowMaterial;
                            if (material != null && material.color != targetColor)
                                material.color = targetColor;
                        }
                        if (entry.ArrowTipMaterial != null)
                        {
                            var material = entry.ArrowTipMaterial;
                            if (material != null && material.color != targetColor)
                                material.color = targetColor;
                        }
                    }
                }
                // ギズモ表示もShowMarkerと連動
                if (entry.Gizmo != null)
                {
                    bool showGizmo = li.ShowGizmo;
                    entry.Gizmo.SetVisible(showGizmo);
                    entry.Gizmo.SetSizeMultiplier(li.GizmoSize * 0.25f);
                }
            }
        }

        internal void ResetVideoLinkBeatState()
        {
            // 現在はBeatSync側ロジックをそのまま参照するため、ローカル状態は保持しない。
        }

        private void UpdateFollowCameraPosition(LightEntry entry, float dt)
        {
            var li = entry.Settings;
            var go = entry.Go;

            // 位置: 公転 or 通常オフセット
            if (li.RevolutionEnabled)
            {
                li.RevolutionAngleDeg = (li.RevolutionAngleDeg + li.RevolutionSpeed * dt) % 360f;
                float rev = li.RevolutionAngleDeg * Mathf.Deg2Rad;
                Vector3 refPos = GetReferencePosition();
                go.transform.position = refPos + new Vector3(
                    Mathf.Sin(rev) * li.RevolutionRadius,
                    li.OffsetY,
                    Mathf.Cos(rev) * li.RevolutionRadius);
            }
            else
            {
                Vector3 desired = GetReferencePosition()
                    + new Vector3(li.OffsetX, li.OffsetY, li.OffsetZ);
                go.transform.position = desired;
                entry.LastDesiredPos  = desired;
            }

            // 照射方向: 自転 > LookAtFemale/カメラ追従 の優先順
            if (li.RotationEnabled)
            {
                li.RotationAngleDeg = (li.RotationAngleDeg + li.RotationSpeed * dt) % 360f;
                go.transform.rotation = Quaternion.Euler(0f, li.RotationAngleDeg, 0f);
            }
            else
            {
                Vector3? lookTarget = null;
                if (li.LookAtFemale)
                {
                    Vector3 femalePos = GetFemalePosition();
                    if (femalePos != Vector3.zero)
                        lookTarget = femalePos;
                }
                if (lookTarget == null)
                {
                    Camera cam = Camera.main;
                    if (cam != null) lookTarget = cam.transform.position;
                }
                if (lookTarget.HasValue)
                {
                    go.transform.LookAt(lookTarget.Value);
                    if (li.LookAtFemale && (li.LookAtOffsetX != 0f || li.LookAtOffsetY != 0f || li.LookAtOffsetZ != 0f))
                        go.transform.rotation *= Quaternion.Euler(li.LookAtOffsetX, li.LookAtOffsetY, li.LookAtOffsetZ);
                }
            }
        }

        private Vector3 GetFemalePosition()
        {
            if (_femalePositionFrame == Time.frameCount)
                return _femalePositionThisFrame;
            _femalePositionFrame = Time.frameCount;
            _femalePositionThisFrame = Vector3.zero;
            if (_hSceneProc == null) return Vector3.zero;
            try
            {
                var lst = FemaleListField?.GetValue(_hSceneProc) as List<ChaControl>;
                if (lst == null || lst.Count == 0) return Vector3.zero;
                var cha = lst[0];
                if (cha == null) return Vector3.zero;
                // 頭ボーン優先、なければ本体Transform
                Transform root = cha.objBodyBone != null ? cha.objBodyBone.transform : null;
                if (_femalePositionChara != cha || _femaleHeadRoot != root)
                {
                    _femalePositionChara = cha;
                    _femaleHeadRoot = root;
                    _femaleHeadBone = null;
                }
                if (_femaleHeadBone == null && root != null)
                    _femaleHeadBone = root.Find("cf_j_root/cf_n_height/cf_j_hips/cf_j_spine01/cf_j_spine02/cf_j_spine03/cf_j_neck/cf_j_head");
                _femalePositionThisFrame = _femaleHeadBone != null ? _femaleHeadBone.position : cha.transform.position;
                return _femalePositionThisFrame;
            }
            catch { return Vector3.zero; }
        }

        // ── ギズモ自由配置 ───────────────────────────────────────────────────

        private void SyncFreeGizmoPositions()
        {
            // WorldPos は常に go.transform.position と同期する。
            // スキップなし ─ これが「保存位置が古くなる」バグの根本修正。
            foreach (var entry in _lightEntries)
            {
                if (entry.Go == null) continue;
                var li = entry.Settings;
                // 所属フォルダがあれば WorldPos はフォルダ基準ローカル、未所属はワールド座標として保持
                bool inFolder = !string.IsNullOrEmpty(li.FolderId) && entry.Go.transform.parent != null;
                Vector3 pos = inFolder ? entry.Go.transform.localPosition : entry.Go.transform.position;
                li.WorldPosX = pos.x;
                li.WorldPosY = pos.y;
                li.WorldPosZ = pos.z;

                // 自由配置かつ固定向きモード時は回転も同期して、ギズモ操作後に戻らないようにする。
                if (!li.FollowCamera && !li.RevolutionEnabled && !li.RotationEnabled && !li.LookAtFemale)
                {
                    Vector3 euler = inFolder ? entry.Go.transform.localEulerAngles : entry.Go.transform.rotation.eulerAngles;
                    li.RotX = euler.x;
                    li.RotY = euler.y;
                    li.RotZ = euler.z;
                }
            }
        }

        private void TryAttachGizmo(LightEntry entry)
        {
            if (!TransformGizmoApi.IsAvailable)
            {
                _log.Warn($"[Gizmo] IsAvailable=false id={entry.Settings.Id}");
                return;
            }
            if (entry.Gizmo != null) return;

            bool ok = TransformGizmoApi.TryAttach(entry.Go, out entry.Gizmo);
            _log.Info($"[Gizmo] TryAttach id={entry.Settings.Id} ok={ok} gizmoNull={entry.Gizmo == null} showMarker={entry.Settings.ShowMarker}");
            if (ok && entry.Gizmo != null)
            {
                bool showGizmo = entry.Settings.ShowGizmo;
                entry.Gizmo.SetVisible(showGizmo);
                entry.Gizmo.DragStateChanged += OnGizmoDragStateChanged;
                _log.Info($"[Gizmo] SetVisible({showGizmo}) id={entry.Settings.Id} IsVisible={entry.Gizmo.IsVisible}");
            }
        }

        private void TryDetachGizmo(LightEntry entry)
        {
            if (entry.Gizmo == null) return;
            entry.Gizmo.DragStateChanged -= OnGizmoDragStateChanged;
            Destroy(entry.Gizmo);
            entry.Gizmo = null;
        }

        // ── 外部から呼ぶAPI（UI/Preset） ────────────────────────────────────

        internal LightEntry AddLight()
        {
            Vector3 spawnPos = GetReferencePosition()
                + GetReferenceRotation() * new Vector3(0f, 0.5f, 2f);
            // 新規ライトは未所属（ワールド座標）。後でUIからフォルダに割り当て可能。
            var li = new LightInstanceSettings
            {
                Id        = GenerateId(),
                Name      = $"Light {_settings.Lights.Count + 1}",
                WorldPosX = spawnPos.x,
                WorldPosY = spawnPos.y,
                WorldPosZ = spawnPos.z,
            };
            _settings.Lights.Add(li);
            SaveSettingsNow("light-add");

            if (!_insideHScene) return null;
            return CreateLightEntry(li);
        }

        internal void RemoveLight(int index)
        {
            EventType eventType = Event.current != null ? Event.current.type : EventType.Ignore;
            EventType eventRawType = Event.current != null ? Event.current.rawType : EventType.Ignore;
            _log.Info($"[Lights] remove-request index={index} event={eventType} raw={eventRawType} count={_settings?.Lights?.Count ?? 0}");

            if (index < 0 || index >= _settings.Lights.Count) return;
            var li = _settings.Lights[index];

            // ランタイムエントリを削除
            for (int i = _lightEntries.Count - 1; i >= 0; i--)
            {
                if (_lightEntries[i].Settings == li)
                {
                    DestroyLightEntry(_lightEntries[i]);
                    _lightEntries.RemoveAt(i);
                    break;
                }
            }
            _settings.Lights.RemoveAt(index);
            SaveSettingsNow("light-remove");
        }

        internal void SetRevolutionEnabled(LightEntry entry, bool enabled)
        {
            var li = entry.Settings;
            if (li.RevolutionEnabled == enabled) return;

            if (enabled && !li.FollowCamera && entry.Go != null)
            {
                // 公転ONにした瞬間の位置を公転中心として確定（フォルダ内ならフォルダ基準ローカル）
                bool inFolder = !string.IsNullOrEmpty(li.FolderId) && entry.Go.transform.parent != null;
                var pos = inFolder ? entry.Go.transform.localPosition : entry.Go.transform.position;
                li.RevolutionCenterX = pos.x;
                li.RevolutionCenterY = pos.y;
                li.RevolutionCenterZ = pos.z;
                TryDetachGizmo(entry);
            }
            else if (!enabled && !li.FollowCamera)
            {
                // 公転OFF: ギズモ再アタッチのみ。位置は WorldPos と同期済みなのでその場に留まる
                TryAttachGizmo(entry);
            }

            li.RevolutionEnabled = enabled;
        }

        internal void SetLightFollowCamera(LightEntry entry, bool follow)
        {
            if (entry.Settings.FollowCamera == follow) return;
            entry.Settings.FollowCamera = follow;

            // WorldPos は SyncFreeGizmoPositions が常に同期しているため、
            // ここで位置を保存・復元する必要はない。GO はその場に留まる。
            if (follow)
                TryDetachGizmo(entry);
            else
                TryAttachGizmo(entry);
        }

        internal LightEntry FindEntry(LightInstanceSettings li)
        {
            foreach (var e in _lightEntries)
                if (e.Settings == li) return e;
            return null;
        }

        // ── ミラーボールCookie ──────────────────────────────────────────────

        private void UpdateMirrorballCookie(LightEntry entry, float dt, float now)
        {
            if (entry == null || entry.Light == null) return;
            var mb = entry.Settings?.Mirrorball;
            if (mb == null || !mb.Enabled)
            {
                if (entry.Light.cookie != null) entry.Light.cookie = null;
                if (entry.MirrorballCookieTexture != null || entry.MirrorballDotTexture != null)
                    ReleaseMirrorballCookie(entry);
                return;
            }

            mb.Resolution = QuantizeCookieResolution(mb.Resolution);
            mb.DotCount   = Mathf.Clamp(mb.DotCount, 1, 4096);
            mb.DotSize    = Mathf.Clamp(mb.DotSize, 0.002f, 0.45f);
            mb.Scatter    = Mathf.Clamp01(mb.Scatter);
            mb.Softness   = Mathf.Clamp01(mb.Softness);
            mb.SpinSpeed  = Mathf.Clamp(mb.SpinSpeed, -3f, 3f);
            mb.UpdateHz   = Mathf.Clamp(mb.UpdateHz, 0.5f, 30f);
            mb.Twinkle    = Mathf.Clamp01(mb.Twinkle);

            if (mb.Animate)
                entry.MirrorballCookieSpinDeg = Mathf.Repeat(entry.MirrorballCookieSpinDeg + mb.SpinSpeed * 360f * dt, 360f);

            int cfgHash      = ComputeMirrorballConfigHash(mb);
            bool cfgChanged  = cfgHash != entry.MirrorballCookieConfigHash;
            bool tickRefresh = mb.Animate && now >= entry.MirrorballCookieNextRebuildAt;
            bool needTexture = entry.MirrorballCookieTexture == null;

            if (needTexture || cfgChanged || tickRefresh)
            {
                RebuildMirrorballCookie(entry, now);
                entry.MirrorballCookieConfigHash  = cfgHash;
                entry.MirrorballCookieNextRebuildAt = mb.Animate
                    ? now + (1f / mb.UpdateHz)
                    : float.PositiveInfinity;
            }

            if (entry.Light.cookie != entry.MirrorballCookieTexture)
                entry.Light.cookie = entry.MirrorballCookieTexture;
        }

        private void RebuildMirrorballCookie(LightEntry entry, float now)
        {
            var mb = entry.Settings.Mirrorball;
            int resolution = QuantizeCookieResolution(mb.Resolution);

            if (entry.MirrorballCookieTexture == null ||
                entry.MirrorballCookieTexture.width != resolution ||
                entry.MirrorballCookieTexture.height != resolution)
            {
                ReleaseMirrorballCookie(entry);
                entry.MirrorballCookieTexture = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                {
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Bilinear,
                    name = $"MirrorballCookie_{entry.Settings.Id}"
                };
                entry.MirrorballCookieTexture.useMipMap = false;
                entry.MirrorballCookieTexture.autoGenerateMips = false;
                entry.MirrorballCookieTexture.Create();
            }
            entry.MirrorballCookieTexture.filterMode = mb.Softness < 0.15f ? FilterMode.Point : FilterMode.Bilinear;

            if (!EnsureMirrorballGpuResources(entry, mb.Softness))
                return;

            int dotCount = Mathf.Clamp(mb.DotCount, 1, 4096);
            float halfSize = Mathf.Clamp(mb.DotSize, 0.002f, 0.45f) * 0.5f;
            int grid = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(dotCount)));
            int seed = DeterministicHash32(entry.Settings.Id);

            float spinRad = entry.MirrorballCookieSpinDeg * Mathf.Deg2Rad;
            float cos = Mathf.Cos(spinRad);
            float sin = Mathf.Sin(spinRad);

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = entry.MirrorballCookieTexture;
            GL.Clear(true, true, Color.black);
            GL.PushMatrix();
            GL.LoadOrtho();
            _mirrorballDotMaterial.mainTexture = entry.MirrorballDotTexture;
            _mirrorballDotMaterial.SetPass(0);
            GL.Begin(GL.QUADS);

            for (int i = 0; i < dotCount; i++)
            {
                int gx = i % grid;
                int gy = i / grid;

                float uGrid = (gx + 0.5f) / grid;
                float vGrid = (gy + 0.5f) / grid;
                float uRand = Hash01(seed + 31, i * 2 + 0);
                float vRand = Hash01(seed + 97, i * 2 + 1);

                float u = Mathf.Lerp(uGrid, uRand, mb.Scatter);
                float v = Mathf.Lerp(vGrid, vRand, mb.Scatter);

                // UV中心回転でミラーボールの回転感を作る
                float ox = u - 0.5f;
                float oy = v - 0.5f;
                float rx = ox * cos - oy * sin;
                float ry = ox * sin + oy * cos;
                u = Repeat01(rx + 0.5f);
                v = Repeat01(ry + 0.5f);

                float brightness = 1f;
                if (mb.Animate && mb.Twinkle > 0.0001f)
                {
                    float phaseOffset = Hash01(seed + 193, i) * Mathf.PI * 2f;
                    float speed       = Mathf.Lerp(0.7f, 1.8f, Hash01(seed + 211, i));
                    float wave01      = (Mathf.Sin((now * 7.5f * speed) + phaseOffset) + 1f) * 0.5f;
                    brightness = Mathf.Lerp(1f - mb.Twinkle, 1f, wave01);
                }

                DrawGpuDotWrapped(u, v, halfSize, brightness);
            }

            GL.End();
            GL.PopMatrix();
            RenderTexture.active = prev;

        }

        private void ReleaseMirrorballCookie(LightEntry entry)
        {
            if (entry == null) return;
            if (entry.Light != null)
                entry.Light.cookie = null;
            if (entry.MirrorballCookieTexture != null)
            {
                entry.MirrorballCookieTexture.Release();
                Destroy(entry.MirrorballCookieTexture);
            }
            if (entry.MirrorballDotTexture != null)
                Destroy(entry.MirrorballDotTexture);
            entry.MirrorballCookieTexture = null;
            entry.MirrorballDotTexture = null;
            entry.MirrorballCookieConfigHash = 0;
            entry.MirrorballCookieNextRebuildAt = 0f;
            entry.MirrorballCookieSpinDeg = 0f;
            entry.MirrorballDotSoftnessQuant = -1;
        }

        // ── ユーティリティ ───────────────────────────────────────────────────

        private static readonly int[] CookieResolutions = { 64, 128, 256, 512, 1024 };

        internal static int QuantizeCookieResolution(int raw)
        {
            int best = CookieResolutions[0];
            int bestDiff = Mathf.Abs(raw - best);
            for (int i = 1; i < CookieResolutions.Length; i++)
            {
                int d = Mathf.Abs(raw - CookieResolutions[i]);
                if (d < bestDiff)
                {
                    best = CookieResolutions[i];
                    bestDiff = d;
                }
            }
            return best;
        }

        private static int ComputeMirrorballConfigHash(MirrorballCookieSettings mb)
        {
            unchecked
            {
                int h = 17;
                h = (h * 31) + QuantizeCookieResolution(mb.Resolution);
                h = (h * 31) + Mathf.Clamp(mb.DotCount, 1, 4096);
                h = (h * 31) + Mathf.RoundToInt(Mathf.Clamp(mb.DotSize, 0.002f, 0.45f) * 10000f);
                h = (h * 31) + Mathf.RoundToInt(Mathf.Clamp01(mb.Scatter) * 1000f);
                h = (h * 31) + Mathf.RoundToInt(Mathf.Clamp01(mb.Softness) * 1000f);
                h = (h * 31) + (mb.Animate ? 1 : 0);
                h = (h * 31) + Mathf.RoundToInt(Mathf.Clamp(mb.SpinSpeed, -3f, 3f) * 1000f);
                h = (h * 31) + Mathf.RoundToInt(Mathf.Clamp(mb.UpdateHz, 0.5f, 30f) * 100f);
                h = (h * 31) + Mathf.RoundToInt(Mathf.Clamp01(mb.Twinkle) * 1000f);
                return h;
            }
        }

        private bool EnsureMirrorballGpuResources(LightEntry entry, float softness)
        {
            if (_mirrorballDotMaterial == null)
            {
                Shader shader = Shader.Find("Legacy Shaders/Particles/Additive");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                if (shader == null) shader = Shader.Find("Unlit/Transparent");
                if (shader == null) return false;
                _mirrorballDotMaterial = new Material(shader);
            }

            int qSoft = Mathf.RoundToInt(Mathf.Clamp01(softness) * 1000f);
            if (entry.MirrorballDotTexture == null || entry.MirrorballDotSoftnessQuant != qSoft)
            {
                if (entry.MirrorballDotTexture != null)
                    Destroy(entry.MirrorballDotTexture);
                entry.MirrorballDotTexture = BuildDotTexture(64, Mathf.Clamp01(softness));
                entry.MirrorballDotSoftnessQuant = qSoft;
            }

            return true;
        }

        private static Texture2D BuildDotTexture(int size, float softness)
        {
            size = Mathf.Clamp(size, 8, 256);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = softness < 0.15f ? FilterMode.Point : FilterMode.Bilinear,
                name = "MirrorballDot"
            };
            var pixels = new Color32[size * size];
            float cx = (size - 1) * 0.5f;
            float cy = (size - 1) * 0.5f;
            float radius = size * 0.5f;
            float inner = radius * Mathf.Lerp(0.95f, 0.45f, softness);
            float feather = Mathf.Max(0.001f, radius - inner);

            for (int y = 0; y < size; y++)
            {
                int row = y * size;
                for (int x = 0; x < size; x++)
                {
                    float dx = x - cx;
                    float dy = y - cy;
                    float d = Mathf.Sqrt((dx * dx) + (dy * dy));
                    float a = 0f;
                    if (d <= inner) a = 1f;
                    else if (d < radius) a = 1f - ((d - inner) / feather);
                    byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);
                    pixels[row + x] = new Color32(255, 255, 255, b);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private static void DrawGpuDotWrapped(float u, float v, float halfSize, float brightness)
        {
            if (halfSize <= 0f || brightness <= 0f) return;
            float x0 = u - halfSize;
            float x1 = u + halfSize;
            float y0 = v - halfSize;
            float y1 = v + halfSize;

            Color c = new Color(brightness, brightness, brightness, brightness);
            for (int ox = -1; ox <= 1; ox++)
            {
                float sx0 = x0 + ox;
                float sx1 = x1 + ox;
                if (sx1 <= 0f || sx0 >= 1f) continue;

                for (int oy = -1; oy <= 1; oy++)
                {
                    float sy0 = y0 + oy;
                    float sy1 = y1 + oy;
                    if (sy1 <= 0f || sy0 >= 1f) continue;

                    GL.Color(c);
                    GL.TexCoord2(0f, 0f); GL.Vertex3(sx0, sy0, 0f);
                    GL.TexCoord2(1f, 0f); GL.Vertex3(sx1, sy0, 0f);
                    GL.TexCoord2(1f, 1f); GL.Vertex3(sx1, sy1, 0f);
                    GL.TexCoord2(0f, 1f); GL.Vertex3(sx0, sy1, 0f);
                }
            }
        }

        private void ReleaseMirrorballSharedResources()
        {
            if (_mirrorballDotMaterial != null)
            {
                Destroy(_mirrorballDotMaterial);
                _mirrorballDotMaterial = null;
            }
        }

        private static float Repeat01(float value)
        {
            return value - Mathf.Floor(value);
        }

        private static int DeterministicHash32(string text)
        {
            unchecked
            {
                uint h = 2166136261u;
                if (!string.IsNullOrEmpty(text))
                {
                    for (int i = 0; i < text.Length; i++)
                    {
                        h ^= text[i];
                        h *= 16777619u;
                    }
                }
                return (int)h;
            }
        }

        private static float Hash01(int seed, int index)
        {
            unchecked
            {
                uint x = (uint)(seed + (index * 374761393));
                x ^= x >> 13;
                x *= 1274126177u;
                x ^= x >> 16;
                return (x & 0x00FFFFFFu) / 16777215f;
            }
        }

        private static float RGBToHue(float r, float g, float b)
        {
            Color.RGBToHSV(new Color(r, g, b), out float h, out _, out _);
            return h;
        }

        // コーン先端メッシュ生成（apex=+Y方向）
        private static Mesh BuildConeMesh(float baseRadius, float height, int segments)
        {
            var mesh = new Mesh();
            var verts = new Vector3[segments + 2];
            verts[0] = new Vector3(0f, height, 0f); // apex
            verts[1] = new Vector3(0f, 0f, 0f);     // base center
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                verts[i + 2] = new Vector3(Mathf.Cos(a) * baseRadius, 0f, Mathf.Sin(a) * baseRadius);
            }
            var tris = new int[segments * 6];
            int t = 0;
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                tris[t++] = 0; tris[t++] = i + 2;    tris[t++] = next + 2; // side
                tris[t++] = 1; tris[t++] = next + 2; tris[t++] = i + 2;    // base
            }
            mesh.vertices  = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
