using System;
using BepInEx.Bootstrap;
using UnityEngine;

namespace MainGameBlankMapAdd
{
    public sealed partial class Plugin
    {
        private const string VrDildoGuid = "com.kks.maingame.vrdildo";
        private Func<bool> _vrDildoSpawned;
        private Action _vrDildoToggleSpawn;
        private float _vrDildoResolveAt;
        private GUIStyle _vrDildoButtonStyle;

        private void DrawVrDildoButtons(Rect rect)
        {
            if (_vrDildoToggleSpawn == null && Time.unscaledTime >= _vrDildoResolveAt)
            {
                _vrDildoResolveAt = Time.unscaledTime + 3f;
                try
                {
                    if (Chainloader.PluginInfos.TryGetValue(VrDildoGuid, out var info) && info.Instance != null)
                    {
                        var type = info.Instance.GetType();
                        _vrDildoToggleSpawn = (Action)Delegate.CreateDelegate(typeof(Action), info.Instance, type.GetMethod("ToggleSpawn"));
                        _vrDildoSpawned = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), info.Instance, type.GetMethod("IsSpawned"));
                    }
                }
                catch (Exception ex) { LogWarn("VR Dildo bar connection: " + ex.Message); }
            }

            bool original = GUI.enabled;
            GUI.enabled = original && _vrDildoToggleSpawn != null;
            bool spawned = _vrDildoSpawned != null && _vrDildoSpawned();
            if (_vrDildoButtonStyle == null)
                _vrDildoButtonStyle = new GUIStyle(GUI.skin.button) { fontSize = 12, padding = new RectOffset(2, 2, 0, 0), wordWrap = false };
            if (GUI.Button(new Rect(rect.x, rect.y, 58f, rect.height),
                new GUIContent("ディルド", spawned ? "ディルドを削除" : "ディルドを呼び出す"), _vrDildoButtonStyle))
                _vrDildoToggleSpawn?.Invoke();
            GUI.enabled = original;
        }
    }
}
