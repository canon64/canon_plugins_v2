using System;
using BepInEx.Bootstrap;
using UnityEngine;

namespace MainGameBlankMapAdd
{
    public sealed partial class Plugin
    {
        private Func<object[]> _actorCoordinatesRead;
        private Func<object[]> _actorLocationRead;
        private Func<bool> _actorGizmoRead;
        private object[] _actorCoordinates;
        private bool _actorCoordinatesOpen;
        private Rect _actorCoordinatesAnchor, _actorCoordinatesList;
        private Vector2 _actorCoordinatesScroll;
        private Func<int> _clothingRead;
        private Func<bool> _actorCameraRead;
        private Action<int> _clothingWrite;
        private Func<object[]> _vmdBarRead;
        private Action<string, int, float> _vmdBarCommand;
        private object[] _vmdBarState;
        private bool _vmdBarMode, _vmdGroupOpen, _vmdEntryOpen;
        private int _vmdSeenEpoch = -1;
        private float _vmdNextResolve;
        private Vector2 _vmdGroupScroll, _vmdEntryScroll, _transportToggleScroll;
        private void ReadVmdBar()
        {
            try {
                if (_vmdBarRead == null && Time.unscaledTime >= _vmdNextResolve) {
                    _vmdNextResolve = Time.unscaledTime + 3;
                    if (Chainloader.PluginInfos.TryGetValue("com.kks.maingame.vmdplay", out var info)) {
                        var type = info.Instance.GetType();
                        var gizmo = type.GetMethod("GetActorGizmoVisible");
                        if (gizmo != null) _actorGizmoRead = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), gizmo);
                        var locations = type.GetMethod("GetActorLocationState");
                        if (locations != null) _actorLocationRead = (Func<object[]>)Delegate.CreateDelegate(typeof(Func<object[]>), locations);
                        var coordinates = type.GetMethod("GetActorCoordinateChoices");
                        if (coordinates != null) _actorCoordinatesRead = (Func<object[]>)Delegate.CreateDelegate(typeof(Func<object[]>), coordinates);
                        var actorCamera = type.GetMethod("GetActorCameraRequested");
                        if (actorCamera != null) _actorCameraRead = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), actorCamera);
                        var cr=type.GetMethod("GetClothingTarget"); var cw=type.GetMethod("SetClothingTarget");
                        if(cr!=null && cw!=null) { _clothingRead=(Func<int>)Delegate.CreateDelegate(typeof(Func<int>),cr); _clothingWrite=(Action<int>)Delegate.CreateDelegate(typeof(Action<int>),cw); }
                        var read = type.GetMethod("GetPlaybackBarState");
                        var command = type.GetMethod("PlaybackBarCommand");
                        if (read != null && command != null) {
                            _vmdBarRead = (Func<object[]>)Delegate.CreateDelegate(typeof(Func<object[]>), read);
                            _vmdBarCommand = (Action<string, int, float>)Delegate.CreateDelegate(typeof(Action<string, int, float>), command);
                        }
                    }
                }
                _vmdBarState = _vmdBarRead?.Invoke();
                if (_vmdBarState != null && (int)_vmdBarState[2] != _vmdSeenEpoch) {
                    _vmdSeenEpoch = (int)_vmdBarState[2];
                    if ((bool)_vmdBarState[1]) _vmdBarMode = true;
                }
                if (_vmdBarState == null) _vmdBarMode = false;
            } catch (Exception ex) { _vmdBarState = null; _vmdBarMode = false; _vmdBarRead = null; LogWarn("VMD bar: " + ex.Message); }
        }
        private void VmdBarCommand(string command, int index = 0, float value = 0)
        {
            try { _vmdBarCommand?.Invoke(command, index, value); }
            catch (Exception ex) { LogWarn("VMD bar command: " + ex.Message); }
        }
        private float DrawClothingTargets(Rect rect) {
            bool before=GUI.enabled; GUI.enabled=before && _clothingRead!=null;
            int current=_clothingRead?.Invoke() ?? 1;
            string[] labels={"メイン","呼び出し","両方"};
            string[] tips={"衣装・着脱・精液のショートカットをH参加女性に適用", "衣装・着脱・精液のショートカットを呼び出しキャラに適用", "衣装・着脱・精液のショートカットをH参加女性と呼び出しキャラの両方に適用"};
            int[] targets={1,0,2};
            float[] widths={44f,62f,42f};
            float buttonWidth = 72f;
            float coordinateWidth = 110f;
            float x = rect.x;
            for(int i=0;i<3;i++) {
                if(GUI.Toggle(new Rect(x,rect.y,widths[i],22),current==targets[i],new GUIContent(labels[i],tips[i])) && current!=targets[i]) _clothingWrite?.Invoke(targets[i]);
                x += widths[i] + 2;
            }
            GUI.enabled = before && _actorCoordinatesRead != null;
            _actorCoordinatesAnchor = new Rect(x,rect.y,coordinateWidth,22);
            if (GUI.Button(_actorCoordinatesAnchor,new GUIContent("サブ衣装 ▼","呼び出しキャラのコーディネートを選択。VMD・音楽の再生を継続して切り替えます"))) {
                _actorCoordinatesOpen = !_actorCoordinatesOpen;
                if (_actorCoordinatesOpen) _actorCoordinates = _actorCoordinatesRead?.Invoke();
                UpdateUiCaptureState();
            }
            x += coordinateWidth + 4;
            GUI.enabled = before && _actorCameraRead != null;
            bool follow = _actorCameraRead?.Invoke() ?? false;
            if (GUI.Toggle(new Rect(x,rect.y,buttonWidth,22),follow,new GUIContent("サブカメ🔄","呼び出しキャラへのサブカメラ追従をON/OFF"),GUI.skin.button) != follow)
                VmdBarCommand("actor-camera");
            GUI.enabled=before;
            return x + buttonWidth;
        }
        private void DrawActorLocationsBar(Rect rect)
        {
            bool before = GUI.enabled;
            object[] state = _actorLocationRead?.Invoke();
            float x = rect.x;
            GUI.Label(new Rect(x, rect.y, 86, 22), "呼び出しキャラ"); x += 88;
            GUI.enabled = before && state != null && state.Length > 2 && (bool)state[2];
            bool shown = _actorGizmoRead?.Invoke() ?? false;
            if (GUI.Toggle(new Rect(x, rect.y, 48, 21), shown, new GUIContent("ギズモ", "呼び出しキャラの移動・回転。中央はXZ移動。選択中の場所へ自動上書き。未登録なら＋で追加"), GUI.skin.button) != shown) VmdBarCommand("actor-gizmo");
            x += 50;
            GUI.enabled = before && state != null && (bool)state[0];
            if (GUI.Button(new Rect(x, rect.y, 24, 21), new GUIContent("◁", "前の登録場所へ呼び出しキャラを移動"))) VmdBarCommand("actor-place-prev"); x += 26;
            GUI.enabled = before && state != null && state.Length > 2 && (bool)state[2];
            if (GUI.Button(new Rect(x, rect.y, 24, 21), new GUIContent("＋", "現在の位置・向きで位置プリセットを追加し選択"))) VmdBarCommand("actor-place-save"); x += 26;
            GUI.enabled = before && state != null && (bool)state[0];
            if (GUI.Button(new Rect(x, rect.y, 24, 21), new GUIContent("−", "選択中の位置プリセットを削除。キャラは残します"))) VmdBarCommand("actor-place-delete"); x += 26;
            GUI.enabled = before && state != null && (bool)state[0];
            if (GUI.Button(new Rect(x, rect.y, 24, 21), new GUIContent("▷", "次の登録場所へ呼び出しキャラを移動"))) VmdBarCommand("actor-place-next"); x += 26;
            bool actorPresent = state != null && state.Length > 3 && (bool)state[3];
            bool canSummon = state != null && state.Length > 4 && (bool)state[4];
            GUI.enabled = before && _vmdBarCommand != null && (actorPresent || canSummon);
            if (GUI.Button(new Rect(x, rect.y, 24, 21), new GUIContent(actorPresent ? "☓" : "呼",
                actorPresent ? "呼び出しキャラを削除／読込中なら呼出取消" : "現在のHキャラと同じ元カードを呼び出す")))
            {
                _actorCoordinatesOpen = false;
                VmdBarCommand(actorPresent ? "actor-delete" : "summon-current");
            }
            x += 26;
            GUI.enabled = before;
            GUI.Label(new Rect(x, rect.y, Mathf.Max(0, rect.xMax - x), 22), state == null ? "" : (string)state[1]);
        }
        private void DrawActorCoordinatesPopup()
        {
            _actorCoordinatesList = Rect.zero;
            if (!_actorCoordinatesOpen) return;
            var names = _actorCoordinates == null ? new string[0] : (string[])_actorCoordinates[0];
            _actorCoordinatesList = DrawVmdChoices(_actorCoordinatesAnchor, names, ref _actorCoordinatesScroll, i => {
                VmdBarCommand("actor-coordinate", ((int[])_actorCoordinates[1])[i]);
                _actorCoordinatesOpen = false;
            });
            if (Event.current.type == EventType.MouseDown && !_actorCoordinatesAnchor.Contains(Event.current.mousePosition)
                && !_actorCoordinatesList.Contains(Event.current.mousePosition)) _actorCoordinatesOpen = false;
        }
        private string _vmdHoveredHelp;
        private bool VmdHelpButton(Rect rect, string label, string help)
        {
            if (rect.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y)))
                _vmdHoveredHelp = help;
            return GUI.Button(rect, new GUIContent(label, help));
        }
        private void DrawVmdBarMode(ref float x, float y, float height)
        {
            _vmdHoveredHelp = null;
            bool before = GUI.enabled;
            GUI.enabled = _vmdBarState != null;
            string modeHelp = _vmdBarMode
                ? "現在はVMD操作。クリックでVMDを終了してキャラを通常制御へ戻し、通常の音楽・動画操作へ切り替えます。通常音楽は自動再開しません。"
                : "現在は通常の音楽・動画操作。クリックでVMD操作へ切り替えます。一覧からプリセットを選ぶと再生します。";
            if (VmdHelpButton(new Rect(x, y, 70, height), _vmdBarMode ? "VMD" : "通常", modeHelp)) {
                if (_vmdBarMode) VmdBarCommand("stop"); else VmdBarCommand("refresh");
                _vmdBarMode = !_vmdBarMode;
                _vmdGroupOpen = _vmdEntryOpen = _folderDropdownOpen = _videoDropdownOpen = false;
                _playbackSeekDragging = false;
            }
            GUI.enabled = before; x += 76;
        }
        private Rect DrawVmdChoices(Rect anchor, string[] entries, ref Vector2 scroll, Action<int> select)
        {
            float h = Mathf.Min(Mathf.Min(240, Mathf.Max(24, entries.Length * 24)), Mathf.Max(24, anchor.y - 4));
            var rect = new Rect(anchor.x, Mathf.Max(0, anchor.y - h), anchor.width, h);
            GUI.Box(rect, GUIContent.none);
            scroll = GUI.BeginScrollView(rect, scroll, new Rect(0, 0, Mathf.Max(1, rect.width - 20), Mathf.Max(24, entries.Length * 24)));
            for (int i = 0; i < entries.Length; i++)
                if (MarqueeButton(new Rect(0, i * 24, Mathf.Max(1, rect.width - 20), 23), entries[i], "choice-" + i)) select(i);
            if (entries.Length == 0) GUI.Label(new Rect(0, 0, rect.width - 20, 24), "未登録");
            GUI.EndScrollView(); return rect;
        }
        private void DrawVmdTransport(float x, float y, float h, Rect bar, float listY, out Rect groupsRect, out Rect entriesRect)
        {
            var state = _vmdBarState; groupsRect = entriesRect = Rect.zero;
            if (state == null) return;
            bool before = GUI.enabled;
            GUI.enabled = (bool)state[0];
            foreach (string command in new[] { "play", "pause", "stop", "previous", "next" }) {
                bool enabled = GUI.enabled;
                if (command == "previous") GUI.enabled &= (bool)state[11];
                if (command == "next") GUI.enabled &= (bool)state[12];
                string label = command == "previous" ? "前の曲" : command == "next" ? "次の曲" : command;
                string help = command == "play" ? "VMDと音源を再生・再開" : command == "pause" ? "VMDと音源を一時停止"
                    : command == "stop" ? "VMDと音源を終了してキャラを通常制御へ戻す"
                    : command == "previous" ? "前のVMDプリセットを再生" : "次のVMDプリセットを再生";
                if (VmdHelpButton(new Rect(x, y, 56, h), label, help)) VmdBarCommand(command);
                GUI.enabled = enabled; x += 60;
            }
            GUI.enabled = before;
            float width = PlaybackTransportLayout.ChoiceWidth(bar.width);
            var group = new Rect(bar.x + 6, listY, width * 0.5f, h);
            var entry = new Rect(group.xMax + 6, listY, width * 0.75f, h);
            if (PlaybackTransportLayout.WrapTransport(bar.width)) GUI.Label(new Rect(bar.x + 6, y + 26, bar.width - 12, 22), state[13] + " : " + state[3]);
            var groups = (string[])state[7]; var entries = (string[])state[9];
            int groupIndex = (int)state[8], entryIndex = (int)state[10];
            if (GUI.Button(group, groupIndex >= 0 && groupIndex < groups.Length ? groups[groupIndex] : "全プリセット")) {
                _vmdGroupOpen = !_vmdGroupOpen; _vmdEntryOpen = false;
            }
            if (MarqueeButton(entry, entryIndex >= 0 && entryIndex < entries.Length ? state[13] + " : " + entries[entryIndex] : "VMDプリセットを選択", "current-vmd")) {
                _vmdEntryOpen = !_vmdEntryOpen; _vmdGroupOpen = false;
            }
            if (_vmdGroupOpen) groupsRect = DrawVmdChoices(group, groups, ref _vmdGroupScroll, i => { VmdBarCommand("group", i); _vmdGroupOpen = false; });
            if (_vmdEntryOpen) entriesRect = DrawVmdChoices(entry, entries, ref _vmdEntryScroll, i => { VmdBarCommand("select", i); _vmdEntryOpen = false; });
            var e = Event.current;
            if (e != null && e.type == EventType.MouseDown && !group.Contains(e.mousePosition) && !entry.Contains(e.mousePosition) && !groupsRect.Contains(e.mousePosition) && !entriesRect.Contains(e.mousePosition))
                _vmdGroupOpen = _vmdEntryOpen = false;
            if (bar.width >= 650 && GUI.Button(new Rect(x, y, 130, h), (bool)state[14] ? "リストループ ON" : "リストループ OFF")) VmdBarCommand("loop");
        }
    }
}
