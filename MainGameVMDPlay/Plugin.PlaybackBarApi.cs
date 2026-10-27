using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MainGameVMDPlay
{
    public sealed partial class Plugin
    {
        private int _barRevision, _barCachedRevision = -1, _barEpoch;
        private string _barGroup = "";
        private string[] _barGroups = new string[0], _barEntries = new string[0];
        private string _runningMyListName = "";
        private void InvalidateBarCatalog() { _barRevision++; }
        private void UpdateBarCatalog()
        {
            if (_barCachedRevision == _barRevision) return;
            _barGroups = new[] { "全プリセット" }.Concat(_settings.MyLists.Keys.OrderBy(x => x)).ToArray();
            if (_barGroup.Length > 0 && !_settings.MyLists.ContainsKey(_barGroup)) _barGroup = "";
            _barEntries = _barGroup.Length == 0 ? _presetFiles.Select(Path.GetFileNameWithoutExtension).ToArray()
                : (_myListRunning && _runningMyListName == _barGroup ? _myListQueue.ToArray() : _settings.MyLists[_barGroup].ToArray());
            _barCachedRevision = _barRevision;
        }
        private int BarIndex => _myListRunning && _barGroup == _runningMyListName ? _myListIndex
            : Array.IndexOf(_barEntries, Path.GetFileNameWithoutExtension(_activePresetPath ?? ""));
        private bool CanBarStep(int delta)
        {
            UpdateBarCatalog();
            int index = _myListRunning ? _myListIndex : BarIndex;
            int count = _myListRunning ? _myListQueue.Length : _barEntries.Length;
            return PlaylistNavigation.Step(index, count, delta, (_myListRunning || _barGroup.Length > 0) && _settings.MyListLoop) >= 0;
        }
        private void SelectBarEntry(int index)
        {
            UpdateBarCatalog();
            if (index < 0 || index >= _barEntries.Length) return;
            if (_barGroup.Length == 0) { ApplyPreset(Path.Combine(PresetDirectory, _barEntries[index] + ".json")); return; }
            _settings.SelectedMyList = _barGroup;
            _myListQueue = _barEntries.ToArray(); _myListIndex = index;
            _runningMyListName = _barGroup; _myListRunning = true;
            PlayMyListEntry(); InvalidateBarCatalog();
        }
        private void StepBarEntry(int delta)
        {
            if (_myListRunning) { _barGroup = _runningMyListName; InvalidateBarCatalog(); }
            if (!CanBarStep(delta)) return;
            SelectBarEntry(PlaylistNavigation.Step(BarIndex, _barEntries.Length, delta, _barGroup.Length > 0 && _settings.MyListLoop));
        }
        // ABI v1: ready, owns, epoch, title, time, length, volume, groups, groupIndex,
        // entries, entryIndex, canPrevious, canNext, status, myListLoop.
        // Arrays are cached and must be treated as read-only by callers.
        public static object[] GetPlaybackBarState()
        {
            var p = Instance;
            if (p == null || p._settings == null) return null;
            p.UpdateBarCatalog();
            return new object[] { p._available && p._cfgEnabled.Value && p._insideHScene && (p._trialChara != null || p._actorRoutine != null),
                p._vmdOwnsCharacter, p._barEpoch, p._presetName,
                p._seekDragging || p._seekRequested ? p._seekDragValue : p._currentAnimTime,
                p._animLength, p._settings.AudioVolume, p._barGroups,
                p._barGroup.Length == 0 ? 0 : Array.IndexOf(p._barGroups, p._barGroup, 1),
                p._barEntries, p.BarIndex, p.CanBarStep(-1), p.CanBarStep(1),
                p._loadRoutine != null || p._actorRoutine != null ? "準備中" : p._state == PluginState.Playing ? "再生中" : p._state == PluginState.Paused ? "一時停止" : p._presetMessage, p._settings.MyListLoop };
        }
        private ChaControl _barCoordinateActor;
        public static object[] GetActorCoordinateChoices()
        {
            var p = Instance;
            var actor = p == null || p.AstralBlocked ? null : p.ReadyIndependentActor();
            if (p != null) p._barCoordinateActor = actor;
            if (actor == null) return null;
            var names = new System.Collections.Generic.List<string>();
            var slots = new System.Collections.Generic.List<int>();
            for (int i = 0; i < actor.chaFile.coordinate.Length; i++) {
                if (actor.chaFile.coordinate[i] == null) continue;
                names.Add(p.GetActorCoordinateName(actor, i)); slots.Add(i);
            }
            return new object[] { names.ToArray(), slots.ToArray() };
        }
        public static void PlaybackBarCommand(string command, int index, float value)
        {
            var p = Instance; if (p == null || p.AstralBlocked) return;
            switch (command) {
                case "actor-gizmo": p.ToggleActorGizmo(); break;
                case "actor-delete": p.DisposeIndependentActor(); break;
                case "actor-place-save":
                    if (!p.CanChangeActorPlace) break;
                    int number = 1;
                    while (p._actorPlaces.Items.Exists(place => place.Name == "場所" + number)) number++;
                    p._actorPlaceName = "場所" + number;
                    p.StoreActorPlace(false); break;
                case "actor-place-delete": p.DeleteActorPlace(); break;
                case "actor-place-prev": p.StepActorPlace(-1); break;
                case "actor-place-next": p.StepActorPlace(1); break;
                case "actor-coordinate":
                    var actor = p.ReadyIndependentActor();
                    if (actor != null && ReferenceEquals(actor, p._barCoordinateActor)) p.ApplyActorCoordinate(null, index, actor);
                    break;
                case "summon-current": p.RequestCurrentHActor(); break;
                case "actor-camera": p.ToggleActorCameraFromBar(); break;
                case "refresh": p.RefreshPresets(); break;
                case "group":
                    p.UpdateBarCatalog();
                    if (index >= 0 && index < p._barGroups.Length) { p._barGroup = index == 0 ? "" : p._barGroups[index]; p.InvalidateBarCatalog(); }
                    break;
                case "select": p.SelectBarEntry(index); break;
                case "previous": p.StepBarEntry(-1); break;
                case "next": p.StepBarEntry(1); break;
                case "play":
                    p.UpdateBarCatalog();
                    if (p._loadRoutine != null) break;
                    if (!p._vmdOwnsCharacter && p._barEntries.Length > 0) p.SelectBarEntry(Math.Max(0, p.BarIndex));
                    else p.PlaybackPlayAll();
                    break;
                case "pause": if (p._loadRoutine == null) p.PlaybackPause(); break;
                case "stop": p.ReleaseCharacter(); break;
                case "seek":
                    if (!p._vmdOwnsCharacter || p._loadRoutine != null || float.IsNaN(value) || float.IsInfinity(value)) break;
                    p._seekDragValue = Mathf.Clamp(value, 0, p._animLength); p._seekRequested = true;
                    p._seekDragging = Input.GetMouseButton(0); p._currentAnimTime = p._seekDragValue;
                    break;
                case "volume":
                    p._settings.AudioVolume = NormalizeAudioVolume(value); p._audioVolumeText = null;
                    if (p._music != null) p._music.volume = p._settings.AudioVolume;
                    p.QueueAdjustmentSave(); break;
                case "loop": p._settings.MyListLoop = !p._settings.MyListLoop; p.SaveSettings("bar-loop"); break;
            }
        }
    }
}
