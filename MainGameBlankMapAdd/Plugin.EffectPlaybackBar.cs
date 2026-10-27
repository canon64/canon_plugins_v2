using System;
using BepInEx.Bootstrap;
using UnityEngine;

namespace MainGameBlankMapAdd
{
    public sealed partial class Plugin
    {
        private Action _chainToggleSpawn;
        private Func<bool> _chainIsSpawned;
        private float _chainResolveAt;
        private Action _kaleidoscopeToggleManual;
        private Func<bool> _kaleidoscopeIsManual;
        private float _kaleidoscopeResolveAt;

        private void DrawEffectButtons(Rect rect)
        {
            if (_chainToggleSpawn == null && Time.unscaledTime >= _chainResolveAt)
            {
                _chainResolveAt = Time.unscaledTime + 3f;
                try
                {
                    if (Chainloader.PluginInfos.TryGetValue("com.kks.maingame.vrneckchain", out var info) && info.Instance != null)
                    {
                        var type = info.Instance.GetType();
                        _chainToggleSpawn = (Action)Delegate.CreateDelegate(typeof(Action), info.Instance, type.GetMethod("ToggleSpawn"));
                        _chainIsSpawned = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), info.Instance, type.GetMethod("IsSpawned"));
                    }
                }
                catch (Exception ex) { LogWarn("Neck chain bar connection: " + ex.Message); }
            }
            if (_kaleidoscopeToggleManual == null && Time.unscaledTime >= _kaleidoscopeResolveAt)
            {
                _kaleidoscopeResolveAt = Time.unscaledTime + 3f;
                try
                {
                    if (Chainloader.PluginInfos.TryGetValue("com.kks.maingame.kaleidoscope", out var info) && info.Instance != null)
                    {
                        var type = info.Instance.GetType();
                        _kaleidoscopeToggleManual = (Action)Delegate.CreateDelegate(typeof(Action), info.Instance, type.GetMethod("ToggleManualActive"));
                        _kaleidoscopeIsManual = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), info.Instance, type.GetMethod("IsManualActive"));
                    }
                }
                catch (Exception ex) { LogWarn("Kaleidoscope bar connection: " + ex.Message); }
            }

            bool original = GUI.enabled;
            GUI.enabled = original && _chainToggleSpawn != null;
            string chainLabel = _chainIsSpawned != null && _chainIsSpawned() ? "鎖削除" : "鎖呼出";
            if (GUI.Button(new Rect(rect.x, rect.y, 58f, rect.height), new GUIContent(chainLabel, "首輪と鎖を呼び出す／削除")))
                _chainToggleSpawn?.Invoke();

            GUI.enabled = original && _kaleidoscopeToggleManual != null;
            string kaleidoscopeLabel = _kaleidoscopeIsManual != null && _kaleidoscopeIsManual() ? "万華鏡OFF" : "万華鏡ON";
            if (GUI.Button(new Rect(rect.x + 62f, rect.y, 70f, rect.height), new GUIContent(kaleidoscopeLabel, "万華鏡の手動表示を切り替える")))
                _kaleidoscopeToggleManual?.Invoke();
            GUI.enabled = original;
        }
    }
}
