using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MainGameVMDPlay
{
    public sealed partial class Plugin
    {
        private string _myListName = "";
        private string _myListMessage = "";
        private string[] _myListQueue;
        private int _myListIndex;
        private bool _myListRunning, _myListSwitching;
        private Vector2 _myListScroll;
        private void StopMyList() { _myListRunning = false; _myListQueue = null; InvalidateBarCatalog(); }
        private List<string> SelectedMyList {
            get {
                if (_settings.MyLists == null) _settings.MyLists = new Dictionary<string, List<string>>();
                return _settings.SelectedMyList != null && _settings.MyLists.TryGetValue(_settings.SelectedMyList, out var list) ? list : null;
            }
        }
        private void AddPresetToMyList(string path) {
            var list = SelectedMyList;
            if (list == null) { _myListMessage = "先にマイリストを作成・選択してください"; return; }
            list.Add(Path.GetFileNameWithoutExtension(path));
            SaveSettings("mylist-add");
            _myListMessage = "追加: " + Path.GetFileNameWithoutExtension(path);
        }
        private void PlayMyListEntry() {
            if (!_myListRunning || _myListQueue == null || _myListQueue.Length == 0) return;
            string name = _myListQueue[_myListIndex];
            if (string.IsNullOrEmpty(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { StopMyList(); _myListMessage = "無効なプリセット名です"; return; }
            bool accepted;
            _myListSwitching = true;
            try { accepted = ApplyPreset(Path.Combine(PresetDirectory, name + ".json")); }
            finally { _myListSwitching = false; }
            if (!accepted) { StopMyList(); _myListMessage = _presetMessage; return; }
            _myListMessage = (_myListIndex + 1) + "/" + _myListQueue.Length + ": " + name;
        }
        private void FinishMyListEntry() {
            if (!_myListRunning) { ReleaseCharacter(); return; }
            _myListSwitching = true;
            try { ReleaseCharacter(); } finally { _myListSwitching = false; }
            _myListIndex++;
            if (_myListIndex >= _myListQueue.Length) {
                if (!_settings.MyListLoop) { StopMyList(); _myListMessage = "マイリスト再生完了"; return; }
                _myListIndex = 0;
            }
            PlayMyListEntry();
        }
        private void DrawMyLists() {
            var selected = SelectedMyList;
            GUILayout.Label("マイリスト（プリセットを順番に再生）");
            GUILayout.BeginHorizontal();
            _myListName = GUILayout.TextField(_myListName, 80);
            if (GUILayout.Button("新規作成", GUILayout.Width(90))) {
                string name = _myListName.Trim();
                if (name.Length == 0 || _settings.MyLists.ContainsKey(name)) _myListMessage = "空でない未登録の名前を入力してください";
                else { _settings.MyLists[name] = new List<string>(); _settings.SelectedMyList = name; SaveSettings("mylist-create"); }
            }
            GUILayout.EndHorizontal();
            foreach (string name in _settings.MyLists.Keys.OrderBy(x => x).ToArray())
                if (GUILayout.Button((_settings.SelectedMyList == name ? "● " : "") + name)) { _settings.SelectedMyList = name; _myListName = name; SaveSettings("mylist-select"); }
            selected = SelectedMyList;
            bool loop = GUILayout.Toggle(_settings.MyListLoop, "マイリストをループ");
            if (loop != _settings.MyListLoop) { _settings.MyListLoop = loop; SaveSettings("mylist-loop"); }
            if (selected != null) {
                GUILayout.Label("上のプリセット一覧の［＋リスト］で追加。再生中の順番は開始時の内容を使用します。");
                _myListScroll = GUILayout.BeginScrollView(_myListScroll, GUILayout.Height(Mathf.Min(180, selected.Count * 28 + 8)));
                int remove = -1, move = -1, target = -1;
                for (int i = 0; i < selected.Count; i++) {
                    GUILayout.BeginHorizontal(); GUILayout.Label((i + 1) + ". " + selected[i]);
                    if (GUILayout.Button("↑", GUILayout.Width(30)) && i > 0) { move = i; target = i - 1; }
                    if (GUILayout.Button("↓", GUILayout.Width(30)) && i + 1 < selected.Count) { move = i; target = i + 1; }
                    if (GUILayout.Button("外す", GUILayout.Width(50))) remove = i;
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
                if (remove >= 0) { selected.RemoveAt(remove); SaveSettings("mylist-remove"); }
                else if (move >= 0) { string item = selected[move]; selected[move] = selected[target]; selected[target] = item; SaveSettings("mylist-order"); }
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("マイリスト再生")) {
                    if (selected.Count == 0) _myListMessage = "プリセットを追加してください";
                    else { _barGroup = _settings.SelectedMyList; _runningMyListName = _barGroup; _myListQueue = selected.ToArray(); _myListIndex = 0; _myListRunning = true; InvalidateBarCatalog(); PlayMyListEntry(); }
                }
                bool previousEnabled = GUI.enabled;
                GUI.enabled = previousEnabled && _myListRunning && (_settings.MyListLoop || _myListIndex > 0);
                if (GUILayout.Button("前の曲")) { _barGroup = _runningMyListName; InvalidateBarCatalog(); StepBarEntry(-1); }
                GUI.enabled = previousEnabled && _myListRunning && (_settings.MyListLoop || _myListIndex + 1 < _myListQueue.Length);
                if (GUILayout.Button("次の曲")) { _barGroup = _runningMyListName; InvalidateBarCatalog(); StepBarEntry(1); }
                GUI.enabled = previousEnabled;
                if (GUILayout.Button("マイリスト停止")) { StopMyList(); ReleaseCharacter(); }
                if (GUILayout.Button("リスト削除")) { _settings.MyLists.Remove(_settings.SelectedMyList); _settings.SelectedMyList = ""; SaveSettings("mylist-delete"); }
                GUILayout.EndHorizontal();
            }
            if (!string.IsNullOrEmpty(_myListMessage)) GUILayout.Label(_myListMessage);
            if (_myListRunning) GUILayout.Label("連続再生中：各VMDは1回再生。通常の停止・本編に戻す操作で終了します。");
        }
    }
}
