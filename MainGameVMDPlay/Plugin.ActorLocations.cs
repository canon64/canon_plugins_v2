using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using UnityEngine;
namespace MainGameVMDPlay {
 public sealed partial class Plugin {
  [DataContract] private sealed class ActorPlace {
   [DataMember] public string Name;
   [DataMember] public float[] Position;
   [DataMember] public float[] Rotation;
  }
  [DataContract] private sealed class ActorPlaces {
   [DataMember] public List<ActorPlace> Items = new List<ActorPlace>();
   [DataMember] public int Selected = -1;
  }
  private ActorPlaces _actorPlaces = new ActorPlaces();
  private string _actorPlaceName = "場所1";
  private Vector2 _actorPlacesScroll;
  private bool _actorPlaceDirty;
  private float _actorPlaceSaveAt;
  private void AutoSaveActorPlace() {
   if (!CanChangeActorPlace) return;
   int i = _actorPlaces.Selected;
   if (i < 0 || i >= _actorPlaces.Items.Count) return;
   var p = _actorStage.transform.position; var r = _actorStage.transform.eulerAngles;
   var place = _actorPlaces.Items[i];
   if ((ToVector(place.Position)-p).sqrMagnitude < 0.00000001f && Quaternion.Angle(Quaternion.Euler(ToVector(place.Rotation)), Quaternion.Euler(r)) < 0.01f) return;
   place.Position = new[]{p.x,p.y,p.z}; place.Rotation = new[]{r.x,r.y,r.z};
   _actorPlaceDirty = true; _actorPlaceSaveAt = Time.unscaledTime + 0.3f;
   _actorPlacementStatus = "選択中の場所を自動更新しました。";
  }
  private void FlushActorPlaceChanges(bool force = false) {
   if (_actorPlaceDirty && (force || Time.unscaledTime >= _actorPlaceSaveAt)) {
    int i = _actorPlaces.Selected;
    if (i >= 0 && i < _actorPlaces.Items.Count) {
     _actorSavedPosition.Value = ToVector(_actorPlaces.Items[i].Position);
     _actorSavedRotation.Value = ToVector(_actorPlaces.Items[i].Rotation);
     _actorPlacementSaved.Value = true; Config.Save();
    }
    SaveActorPlaces();
   }
  }
  private void DeleteActorPlace() {
   if (!CanChangeActorPlace) return;
   int i = _actorPlaces.Selected;
   if (i < 0 || i >= _actorPlaces.Items.Count) return;
   _actorPlaces.Items.RemoveAt(i); _actorPlaces.Selected = -1;
   SaveActorPlaces();
  }
  private string ActorPlacesPath => Path.Combine(_pluginDir, "ActorLocations.json");
  private void LoadActorPlaces() {
   try {
    if (!File.Exists(ActorPlacesPath)) return;
    using (var stream = File.OpenRead(ActorPlacesPath)) _actorPlaces = (ActorPlaces)new DataContractJsonSerializer(typeof(ActorPlaces)).ReadObject(stream);
    if (_actorPlaces == null) _actorPlaces = new ActorPlaces();
    if (_actorPlaces.Items == null) _actorPlaces.Items = new List<ActorPlace>();
    _actorPlaces.Items.RemoveAll(p => p == null || p.Position == null || p.Rotation == null || p.Position.Length != 3 || p.Rotation.Length != 3 || !IsFinitePlacement(ToVector(p.Position)) || !IsFinitePlacement(ToVector(p.Rotation)));
    if (_actorPlaces.Selected < 0 || _actorPlaces.Selected >= _actorPlaces.Items.Count) _actorPlaces.Selected = -1;
   } catch (Exception ex) { _actorPlaces = new ActorPlaces(); LogWarn("場所読込失敗: " + ex.Message); }
  }
  private static Vector3 ToVector(float[] a) => new Vector3(a[0],a[1],a[2]);
  private void SaveActorPlaces() {
   try {
    string temp = ActorPlacesPath + ".tmp";
    using (var stream = File.Create(temp)) new DataContractJsonSerializer(typeof(ActorPlaces)).WriteObject(stream, _actorPlaces);
    if (File.Exists(ActorPlacesPath)) File.Replace(temp, ActorPlacesPath, null); else File.Move(temp, ActorPlacesPath);
    _actorPlaceDirty = false;
   } catch (Exception ex) { _actorPlacementStatus = "場所保存失敗: " + ex.Message; LogWarn(_actorPlacementStatus); }
  }
  private bool CanChangeActorPlace => !AstralBlocked && _actorStage != null && ReadyIndependentActor() != null;
  public static object[] GetActorLocationState() {
   var p = Instance;
   if (p == null) return new object[] { false, "未登録", false, false, false };
   int i = p._actorPlaces.Selected;
   bool actorPresent = p._actorSession != null || p._actorRoutine != null || p._actorStage != null;
   bool canSummon = !p.AstralBlocked && p._cfgEnabled.Value && p._actorLoaderAvailable && p._insideHScene && p._hActor != null;
   return new object[] { p.CanChangeActorPlace && p._actorPlaces.Items.Count > 0, i >= 0 && i < p._actorPlaces.Items.Count ? p._actorPlaces.Items[i].Name : "場所", p.CanChangeActorPlace, actorPresent, canSummon };
  }
  private void SelectActorPlace(int index) {
   if (!CanChangeActorPlace || index < 0 || index >= _actorPlaces.Items.Count) return;
   var place = _actorPlaces.Items[index];
   var pos = ToVector(place.Position); var rot = ToVector(place.Rotation);
   ApplyActorPlacement(pos, Quaternion.Euler(rot));
   RefreshActorPlacementText();
   _actorSavedPosition.Value = pos; _actorSavedRotation.Value = rot; _actorPlacementSaved.Value = true; Config.Save();
   _actorPlaces.Selected = index; _actorPlaceName = place.Name; SaveActorPlaces();
  }
  private void StepActorPlace(int direction) {
   int count = _actorPlaces.Items.Count;
   if (count == 0) return;
   int index = _actorPlaces.Selected;
   SelectActorPlace(index < 0 ? (direction < 0 ? count - 1 : 0) : (index + (direction < 0 ? -1 : 1) + count) % count);
  }
  private void DrawActorPlaces() {
   GUILayout.Label("場所の登録（位置XYZ・角度XYZ）");
   _actorPlaceName = GUILayout.TextField(_actorPlaceName ?? "");
   bool enabled = GUI.enabled;
   GUI.enabled = enabled && CanChangeActorPlace;
   GUILayout.BeginHorizontal();
   if (GUILayout.Button("＋ 場所を追加")) StoreActorPlace(false);
   int selected = _actorPlaces.Selected;
   GUI.enabled = enabled && CanChangeActorPlace && selected >= 0 && selected < _actorPlaces.Items.Count;
   if (GUILayout.Button("選択場所を上書き")) StoreActorPlace(true);
   if (GUILayout.Button("− 場所を削除")) DeleteActorPlace();
   GUILayout.EndHorizontal();
   GUI.enabled = enabled && CanChangeActorPlace;
   using (var scroll = new GUILayout.ScrollViewScope(_actorPlacesScroll, GUILayout.Height(Mathf.Min(120, 26 * _actorPlaces.Items.Count + 4)))) {
    _actorPlacesScroll = scroll.scrollPosition;
    for (int i=0;i<_actorPlaces.Items.Count;i++) if (GUILayout.Button((i == _actorPlaces.Selected ? "● " : "") + _actorPlaces.Items[i].Name)) SelectActorPlace(i);
   }
   GUI.enabled = enabled;
  }
  private void StoreActorPlace(bool overwrite) {
   if (!CanChangeActorPlace) return;
   string name = (_actorPlaceName ?? "").Trim();
   if (name.Length == 0) { _actorPlacementStatus = "場所名を入力してください。"; return; }
   var p = _actorStage.transform.position; var r = _actorStage.transform.eulerAngles;
   var place = new ActorPlace { Name = name, Position = new[]{p.x,p.y,p.z}, Rotation = new[]{r.x,r.y,r.z} };
   if (overwrite) _actorPlaces.Items[_actorPlaces.Selected] = place;
   else { _actorPlaces.Items.Add(place); _actorPlaces.Selected = _actorPlaces.Items.Count - 1; }
   SaveActorPlaces();
  }
  private bool IsActorPlaceAlreadyStored() {
   if (!CanChangeActorPlace) return false;
   var position = _actorStage.transform.position;
   var rotation = _actorStage.transform.rotation;
   // Ignore transform round-off, and compare rotations without the 0/360 degree discontinuity.
   return _actorPlaces.Items.Exists(place =>
    (ToVector(place.Position) - position).sqrMagnitude <= 0.000001f &&
    Quaternion.Angle(Quaternion.Euler(ToVector(place.Rotation)), rotation) <= 0.1f);
  }
 }
}
