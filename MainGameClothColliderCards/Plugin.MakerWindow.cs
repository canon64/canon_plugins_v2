using Bindings = CanonPlugins.Shared.PluginInput;
using System;
using System.Globalization;
using KKAPI.Maker;
using UnityEngine;

namespace MainGameClothColliderCards
{
    public sealed partial class Plugin
    {
        private bool _makerWindowOpen;
        private Rect _makerWindow = new Rect(30, 90, 440, 570);
        private Vector2 _boneScroll;
        private Vector2 _recordScroll;
        private string _boneFilter = "cf_J_";
        private string _selectedBone;
        private int _selectedRecord = -1;
        private string[] _fields;
        private string _message = "F6: collider editor";

        private void Update()
        {
            if (MakerAPI.InsideAndLoaded && Bindings.Down("MainGameClothColliderCards.toggle", "F6"))
                _makerWindowOpen = !_makerWindowOpen;
        }

        private void OnGUI()
        {
            if (!_makerWindowOpen || !MakerAPI.InsideAndLoaded) return;
            _makerWindow = GUI.Window(632181, _makerWindow, DrawMakerWindow, "Card Cloth Colliders");
        }

        private void DrawMakerWindow(int id)
        {
            var cha = MakerAPI.GetCharacterControl();
            var controller = cha == null ? null : cha.GetComponent<ColliderCardController>();
            if (controller == null)
            {
                GUILayout.Label("Character is loading.");
                GUI.DragWindow();
                return;
            }

            GUILayout.Label("Saved on the character card. Select a bone:");
            _boneFilter = GUILayout.TextField(_boneFilter ?? "");
            _boneScroll = GUILayout.BeginScrollView(_boneScroll, GUILayout.Height(135));
            int shown = 0;
            foreach (var bone in cha.GetComponentsInChildren<Transform>(true))
            {
                if (bone == cha.transform || bone.name == "ClothColliderCards") continue;
                if (!string.IsNullOrEmpty(_boneFilter) && bone.name.IndexOf(_boneFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                string path = ColliderCardController.GetBonePath(cha, bone);
                if (path == null) continue;
                if (GUILayout.Button((path == _selectedBone ? "● " : "") + bone.name)) _selectedBone = path;
                if (++shown >= 80) break;
            }
            GUILayout.EndScrollView();
            GUILayout.Label("Bone: " + (_selectedBone ?? "(none)"));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Add sphere")) Add(controller, ColliderShape.Sphere);
            if (GUILayout.Button("Add capsule")) Add(controller, ColliderShape.Capsule);
            GUILayout.EndHorizontal();

            GUILayout.Label("Colliders on this card: " + controller.Records.Count);
            bool flare = GUILayout.Toggle(controller.FlareSkirtACloth, "Flare skirt A Cloth Trial: enable cloth");
            if (flare != controller.FlareSkirtACloth) controller.SetFlareSkirtACloth(flare);
            _recordScroll = GUILayout.BeginScrollView(_recordScroll, GUILayout.Height(120));
            for (int i = 0; i < controller.Records.Count; i++)
            {
                var record = controller.Records[i];
                GUILayout.BeginHorizontal();
                if (GUILayout.Button((i == _selectedRecord ? "● " : "") + (i + 1) + " " + record.Shape + " " + ShortBone(record.BonePath)))
                    SelectRecord(i, record);
                if (GUILayout.Button("X", GUILayout.Width(28)))
                {
                    controller.RemoveCollider(i);
                    _selectedRecord = -1;
                    _fields = null;
                    break;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            if (_selectedRecord >= 0 && _selectedRecord < controller.Records.Count && _fields != null)
            {
                GUILayout.Label("Local position X / Y / Z");
                DrawTriple(0);
                GUILayout.Label("Local rotation X / Y / Z (degrees)");
                DrawTriple(3);
                GUILayout.Label("Radius / Height / Capsule axis (0=X, 1=Y, 2=Z)");
                DrawTriple(6);
                if (GUILayout.Button("Apply values")) Apply(controller);
            }
            GUILayout.Label(_message);
            GUI.DragWindow(new Rect(0, 0, _makerWindow.width, 22));
        }

        private void Add(ColliderCardController controller, ColliderShape shape)
        {
            if (string.IsNullOrEmpty(_selectedBone)) { _message = "Select a bone first."; return; }
            var record = new ColliderRecord
            {
                BonePath = _selectedBone,
                Shape = shape,
                Radius = 0.08f,
                Height = 0.3f,
                Direction = 1
            };
            if (!controller.AddCollider(record)) { _message = "Could not add collider."; return; }
            SelectRecord(controller.Records.Count - 1, record);
            _message = "Added. Save the character card to keep it.";
        }

        private void SelectRecord(int index, ColliderRecord record)
        {
            _selectedRecord = index;
            _fields = new[]
            {
                Text(record.LocalPosition.x), Text(record.LocalPosition.y), Text(record.LocalPosition.z),
                Text(record.LocalEulerAngles.x), Text(record.LocalEulerAngles.y), Text(record.LocalEulerAngles.z),
                Text(record.Radius), Text(record.Height), record.Direction.ToString(CultureInfo.InvariantCulture)
            };
        }

        private void DrawTriple(int start)
        {
            GUILayout.BeginHorizontal();
            for (int i = start; i < start + 3; i++)
                _fields[i] = GUILayout.TextField(_fields[i], GUILayout.Width(130));
            GUILayout.EndHorizontal();
        }

        private void Apply(ColliderCardController controller)
        {
            var values = new float[8];
            for (int i = 0; i < values.Length; i++)
                if (!float.TryParse(_fields[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                { _message = "Use decimal numbers with a dot."; return; }
            if (!int.TryParse(_fields[8], out int axis)) { _message = "Axis must be 0, 1, or 2."; return; }
            var record = controller.Records[_selectedRecord].Copy();
            record.LocalPosition = new Vector3(values[0], values[1], values[2]);
            record.LocalEulerAngles = new Vector3(values[3], values[4], values[5]);
            record.Radius = values[6];
            record.Height = values[7];
            record.Direction = axis;
            _message = controller.ReplaceCollider(_selectedRecord, record) ? "Saved in memory; save the card." : "Invalid collider values.";
        }

        private static string Text(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);
        private static string ShortBone(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash < 0 ? path : path.Substring(slash + 1);
        }
    }
}
