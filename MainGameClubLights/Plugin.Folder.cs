using Bindings = CanonPlugins.Shared.PluginInput;
using System;
using System.Collections.Generic;
using MainGameTransformGizmo;
using UnityEngine;
using Valve.VR;
using VRGIN.Controls;
using VRGIN.Core;

namespace MainGameClubLights
{
    // フォルダ（ライトをまとめる親）。複数自由に作成でき、所属ライトだけを一括移動する。
    // 親子化は SetParent(worldPositionStays:true) で行い、入れた瞬間に位置が原点へ集約しないようにする。
    public sealed partial class Plugin
    {
        internal sealed class FolderEntry
        {
            public LightFolder    Settings;
            public GameObject     Go;
            public GameObject     Handle;
            public Renderer       HandleRenderer;
            public TransformGizmo Gizmo;
        }

        private readonly List<FolderEntry> _folderEntries = new List<FolderEntry>();

        // フォルダのVR掴み
        private FolderEntry     _grabbedFolder;
        private Controller      _folderVrController;
        private Controller.Lock _folderVrFocusLock = Controller.Lock.Invalid;
        private Vector3         _folderVrGrabOffset;
        // トリガー併用での角度変更
        private bool            _folderRotating;
        private Quaternion      _folderRotRefController;
        private Quaternion      _folderRotRefFolder;

        // ── 生成 / 破棄 ──────────────────────────────────────────────────────

        private void EnsureFolders()
        {
            if (_settings.Folders == null) _settings.Folders = new List<LightFolder>();
            foreach (var f in _settings.Folders)
            {
                if (f == null) continue;
                if (string.IsNullOrEmpty(f.Id)) f.Id = GenerateId();
                // 旧データ移行: 未設定(0)や巨大値を既定の小さめサイズへ
                if (f.HandleSize <= 0f) f.HandleSize = 0.08f;
                if (f.GizmoSize  <= 0f) f.GizmoSize  = 0.5f;
                if (FindFolderEntry(f.Id) == null)
                    CreateFolderEntry(f);
            }
        }

        private FolderEntry CreateFolderEntry(LightFolder f)
        {
            var go = new GameObject($"ClubLightsFolder_{f.Id}");
            go.transform.position = new Vector3(f.PosX, f.PosY, f.PosZ);
            go.transform.rotation = Quaternion.Euler(f.RotX, f.RotY, f.RotZ);

            // 掴みハンドル（黄球。実体が無いので位置を可視化）
            var handle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            handle.name = "FolderHandle";
            handle.transform.SetParent(go.transform, worldPositionStays: false);
            handle.transform.localPosition = Vector3.zero;
            handle.transform.localScale    = Vector3.one * Mathf.Max(0.02f, f.HandleSize);
            var col = handle.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var mr = handle.GetComponent<Renderer>();
            if (mr != null)
            {
                mr.material = new Material(Shader.Find("Unlit/Color"));
                mr.material.color = new Color(1f, 0.85f, 0.2f, 1f);
            }
            handle.layer = 0;
            handle.SetActive(f.ShowHandle);

            var entry = new FolderEntry { Settings = f, Go = go, Handle = handle, HandleRenderer = mr };

            if (TransformGizmoApi.IsAvailable && TransformGizmoApi.TryAttach(go, out entry.Gizmo) && entry.Gizmo != null)
            {
                entry.Gizmo.DragStateChanged += OnGizmoDragStateChanged;
                entry.Gizmo.SetSizeMultiplier(Mathf.Max(0.05f, f.GizmoSize));
                entry.Gizmo.SetVisible(f.ShowHandleGizmo);
            }
            _folderEntries.Add(entry);
            _log.Info($"[Folder] created id={f.Id} name={f.Name} pos={go.transform.position} showHandle={f.ShowHandle} gizmoNull={entry.Gizmo == null}");
            return entry;
        }

        private void DestroyFolderEntry(FolderEntry entry)
        {
            if (entry == null) return;
            if (entry.Gizmo != null)
            {
                entry.Gizmo.DragStateChanged -= OnGizmoDragStateChanged;
                Destroy(entry.Gizmo);
                entry.Gizmo = null;
            }
            if (entry.Go != null)
                Destroy(entry.Go);
        }

        private void DestroyFolders()
        {
            StopFolderVrGrab();
            foreach (var fe in _folderEntries)
                DestroyFolderEntry(fe);
            _folderEntries.Clear();
        }

        // 毎フレーム: ハンドル/ギズモ表示同期 ＋ フォルダ transform を設定へ書き戻し
        private void UpdateFolders()
        {
            foreach (var fe in _folderEntries)
            {
                if (fe.Go == null || fe.Settings == null) continue;
                var f = fe.Settings;

                if (fe.Handle != null)
                {
                    if (fe.Handle.activeSelf != f.ShowHandle)
                        fe.Handle.SetActive(f.ShowHandle);
                    Vector3 handleScale = Vector3.one * Mathf.Max(0.02f, f.HandleSize);
                    if (fe.Handle.transform.localScale != handleScale)
                        fe.Handle.transform.localScale = handleScale;
                }
                if (fe.Gizmo != null)
                {
                    fe.Gizmo.SetSizeMultiplier(Mathf.Max(0.05f, f.GizmoSize));
                    fe.Gizmo.SetVisible(f.ShowHandleGizmo);
                }

                Vector3 p = fe.Go.transform.position;
                Vector3 e = fe.Go.transform.rotation.eulerAngles;
                f.PosX = p.x; f.PosY = p.y; f.PosZ = p.z;
                f.RotX = e.x; f.RotY = e.y; f.RotZ = e.z;
            }
        }

        // ── 検索 ─────────────────────────────────────────────────────────────

        private FolderEntry FindFolderEntry(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var fe in _folderEntries)
                if (fe.Settings != null && fe.Settings.Id == id) return fe;
            return null;
        }

        private Transform FindFolderGo(string id)
        {
            var fe = FindFolderEntry(id);
            return fe != null && fe.Go != null ? fe.Go.transform : null;
        }

        private LightFolder FindFolder(string id)
        {
            if (string.IsNullOrEmpty(id) || _settings.Folders == null) return null;
            foreach (var f in _settings.Folders)
                if (f != null && f.Id == id) return f;
            return null;
        }

        private static Matrix4x4 FolderMatrix(LightFolder f)
        {
            return Matrix4x4.TRS(
                new Vector3(f.PosX, f.PosY, f.PosZ),
                Quaternion.Euler(f.RotX, f.RotY, f.RotZ),
                Vector3.one);
        }

        // ── 作成 / 削除（UI） ──────────────────────────────────────────────

        internal LightFolder CreateFolder()
        {
            if (_settings.Folders == null) _settings.Folders = new List<LightFolder>();
            Vector3 spawn = GetReferencePosition() + GetReferenceRotation() * new Vector3(0f, 0.5f, 2f);
            var f = new LightFolder
            {
                Id   = GenerateId(),
                Name = $"Folder {_settings.Folders.Count + 1}",
                PosX = spawn.x, PosY = spawn.y, PosZ = spawn.z,
            };
            _settings.Folders.Add(f);
            if (_insideHScene) CreateFolderEntry(f);
            SaveSettingsNow("folder-add");
            return f;
        }

        internal void RemoveFolder(string id)
        {
            var f = FindFolder(id);
            if (f == null) return;

            // 所属ライトを未所属へ（ワールド位置は保ったまま）
            foreach (var li in _settings.Lights)
                if (li != null && li.FolderId == id)
                    AssignLightToFolder(li, "");

            var fe = FindFolderEntry(id);
            if (fe != null)
            {
                DestroyFolderEntry(fe);
                _folderEntries.Remove(fe);
            }
            _settings.Folders.Remove(f);
            SaveSettingsNow("folder-remove");
            _log.Info($"[Folder] removed id={id} name={f.Name}");
        }

        // ── ライトのフォルダ割り当て（位置を保ったまま） ────────────────────

        internal void AssignLightToFolder(LightInstanceSettings li, string newFolderId)
        {
            if (li == null) return;
            newFolderId = newFolderId ?? "";
            if (li.FolderId == newFolderId) return;

            string oldFolderId = li.FolderId;

            // 公転中心も同じ frame 変換で保つ（割り当てで軌道が飛ばないように）
            Vector3 newCenter = ConvertPointBetweenFolders(
                new Vector3(li.RevolutionCenterX, li.RevolutionCenterY, li.RevolutionCenterZ),
                oldFolderId, newFolderId);

            LightEntry entry = FindEntry(li);
            if (entry != null && entry.Go != null)
            {
                // 実体あり: worldPositionStays:true で位置を1ミリも動かさず親付け替え
                Transform newParent = FindFolderGo(newFolderId);
                entry.Go.transform.SetParent(newParent, worldPositionStays: true);
                li.FolderId = newFolderId;
                Vector3 p = newParent != null ? entry.Go.transform.localPosition : entry.Go.transform.position;
                li.WorldPosX = p.x; li.WorldPosY = p.y; li.WorldPosZ = p.z;
            }
            else
            {
                // 実体なし（Hシーン外）: フォルダ行列でワールド↔ローカル変換して位置を保つ
                Vector3 world = GetLightWorldFromData(li);
                li.FolderId = newFolderId;
                LightFolder nf = FindFolder(newFolderId);
                Vector3 stored = nf != null ? FolderMatrix(nf).inverse.MultiplyPoint3x4(world) : world;
                li.WorldPosX = stored.x; li.WorldPosY = stored.y; li.WorldPosZ = stored.z;
            }

            li.RevolutionCenterX = newCenter.x;
            li.RevolutionCenterY = newCenter.y;
            li.RevolutionCenterZ = newCenter.z;
            SaveSettingsNow("light-folder-assign");
        }

        // 点 p を fromFolder のローカル空間→toFolder のローカル空間へ変換（空=ワールド）
        private Vector3 ConvertPointBetweenFolders(Vector3 p, string fromFolderId, string toFolderId)
        {
            LightFolder from = FindFolder(fromFolderId);
            Vector3 world = from != null ? FolderMatrix(from).MultiplyPoint3x4(p) : p;
            LightFolder to = FindFolder(toFolderId);
            return to != null ? FolderMatrix(to).inverse.MultiplyPoint3x4(world) : world;
        }

        private Vector3 GetLightWorldFromData(LightInstanceSettings li)
        {
            Vector3 stored = new Vector3(li.WorldPosX, li.WorldPosY, li.WorldPosZ);
            LightFolder cur = FindFolder(li.FolderId);
            return cur != null ? FolderMatrix(cur).MultiplyPoint3x4(stored) : stored;
        }

        // フォルダ内包ライトのマーカー一括表示/非表示
        internal void SetFolderLightsMarker(string folderId, bool show)
        {
            if (string.IsNullOrEmpty(folderId)) return;
            int n = 0;
            foreach (var li in _settings.Lights)
            {
                if (li != null && li.FolderId == folderId)
                {
                    li.ShowMarker = show;
                    n++;
                }
            }
            SaveSettingsNow("folder-markers-bulk");
            _log.Info($"[Folder] markers bulk folder={folderId} show={show} count={n}");
        }

        // フォルダに所属するライトが1つ以上あり、全てマーカー表示中か
        internal bool AreFolderLightMarkersOn(string folderId)
        {
            if (string.IsNullOrEmpty(folderId)) return false;
            bool any = false;
            foreach (var li in _settings.Lights)
            {
                if (li == null || li.FolderId != folderId) continue;
                any = true;
                if (!li.ShowMarker) return false;
            }
            return any;
        }

        // UI用: 所属フォルダ名（未所属/不明は「未所属」）
        internal string GetFolderName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "未所属";
            var f = FindFolder(id);
            return f != null ? f.Name : "未所属";
        }

        // UI用: 未所属("") → Folders[0] → … → 末尾 → 未所属 をサイクル
        internal string NextFolderId(string currentId)
        {
            var folders = _settings.Folders;
            if (folders == null || folders.Count == 0) return "";
            if (string.IsNullOrEmpty(currentId)) return folders[0].Id;
            int idx = folders.FindIndex(f => f != null && f.Id == currentId);
            if (idx < 0 || idx + 1 >= folders.Count) return "";
            return folders[idx + 1].Id;
        }

        // ── VR掴み（フォルダ） ───────────────────────────────────────────────

        private bool TryBeginFolderGrab(Controller ctrl)
        {
            if (ctrl == null || ctrl.Input == null) return false;
            if (!Bindings.VrDown("MainGameClubLights.grab", ctrl.Input, "Grip")) return false;

            Transform ctrlTf = ((Component)ctrl).transform;
            FolderEntry best = null;
            float minDist = float.MaxValue;
            foreach (var fe in _folderEntries)
            {
                if (fe.Go == null || fe.Settings == null) continue;
                if (!fe.Settings.ShowHandle && !fe.Settings.ShowHandleGizmo) continue; // どちらも非表示なら掴めない
                float dist = Vector3.Distance(ctrlTf.position, fe.Go.transform.position);
                float reach = Mathf.Max(0.3f, fe.Settings.HandleSize);
                if (dist <= reach && dist < minDist)
                {
                    minDist = dist;
                    best = fe;
                }
            }
            if (best == null) return false;

            if (!ctrl.TryAcquireFocus(out Controller.Lock focusLock) || focusLock == null || !focusLock.IsValid)
            {
                _log.Warn($"[Folder][VRGrab] focus acquire failed controller={ctrl.name}");
                return false;
            }

            _grabbedFolder      = best;
            _folderVrController  = ctrl;
            _folderVrFocusLock   = focusLock;
            _folderVrGrabOffset  = best.Go.transform.position - ctrlTf.position;
            _log.Info($"[Folder][VRGrab] start id={best.Settings.Id} controller={ctrl.name}");
            return true;
        }

        private void UpdateFolderVrGrab()
        {
            if (_grabbedFolder == null) return;
            if (_grabbedFolder.Go == null || _folderVrController == null || _folderVrController.Input == null)
            {
                StopFolderVrGrab();
                return;
            }
            if (!Bindings.VrHeld("MainGameClubLights.grab", _folderVrController.Input, "Grip"))
            {
                StopFolderVrGrab();
                SaveCurrentProfile(out _);
                return;
            }
            Transform ctrlTf = ((Component)_folderVrController).transform;

            // グリップ中は常に「位置だけ」コントローラーに追従
            _grabbedFolder.Go.transform.position = ctrlTf.position + _folderVrGrabOffset;

            // グリップ＋トリガー押下中だけ「角度」をコントローラーの回転に追従
            bool trig = Bindings.VrHeld("MainGameClubLights.rotate", _folderVrController.Input, "Trigger");
            if (trig)
            {
                if (!_folderRotating)
                {
                    // トリガーを引いた瞬間の基準を記録（そこからの回転差分を適用）
                    _folderRotating         = true;
                    _folderRotRefController  = ctrlTf.rotation;
                    _folderRotRefFolder      = _grabbedFolder.Go.transform.rotation;
                }
                Quaternion delta = ctrlTf.rotation * Quaternion.Inverse(_folderRotRefController);
                _grabbedFolder.Go.transform.rotation = delta * _folderRotRefFolder;
            }
            else
            {
                _folderRotating = false;
            }
        }

        private void StopFolderVrGrab()
        {
            if (_grabbedFolder != null)
                UpdateFolders();
            if (_folderVrFocusLock != null && _folderVrFocusLock.IsValid)
            {
                try { _folderVrFocusLock.SafeRelease(); }
                catch (Exception ex) { _log.Warn("[Folder][VRGrab] focus release failed: " + ex.Message); }
            }
            _folderVrFocusLock  = Controller.Lock.Invalid;
            _grabbedFolder      = null;
            _folderVrController = null;
            _folderRotating     = false;
        }
    }
}
