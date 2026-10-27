using System;
using System.Collections.Generic;
using UnityEngine;

namespace MainGameObjectComposer
{
    [Serializable]
    public sealed class ComposerObjectInfo
    {
        public string id;
        public string name;
    }

    public sealed partial class Plugin
    {
        private static Plugin _instance;

        /// <summary>
        /// 外部プラグインから管理オブジェクト一覧を取得。
        /// 未起動時は false。
        /// </summary>
        public static bool TryGetManagedObjectList(out List<ComposerObjectInfo> objects)
        {
            objects = null;
            Plugin inst = _instance;
            if (inst == null || inst._objects == null) return false;

            objects = new List<ComposerObjectInfo>(inst._objects.Count);
            for (int i = 0; i < inst._objects.Count; i++)
            {
                ManagedObjectData d = inst._objects[i];
                if (d == null) continue;
                objects.Add(new ComposerObjectInfo { id = d.id, name = d.name });
            }
            return true;
        }

        /// <summary>
        /// 指定 ID のランタイム Transform を取得。
        /// 未生成 / 削除済み / 未起動の場合は false。
        /// </summary>
        public static bool TryGetManagedObjectTransform(string id, out Transform transform)
        {
            transform = null;
            Plugin inst = _instance;
            if (inst == null || string.IsNullOrEmpty(id)) return false;
            if (!inst._runtimeObjects.TryGetValue(id, out RuntimeObjectRef r)) return false;
            if (r == null || r.GameObject == null) return false;
            transform = r.GameObject.transform;
            return true;
        }

        /// <summary>
        /// 管理オブジェクトの追加・削除・リネーム時に発火。
        /// </summary>
        public static event Action ManagedObjectListChanged;

        internal static void RaiseManagedObjectListChanged()
        {
            try
            {
                ManagedObjectListChanged?.Invoke();
            }
            catch (Exception ex)
            {
                // 購読側の例外で本体が止まらないよう吸収
                _instance?.LogWarn("ManagedObjectListChanged subscriber threw: " + ex.Message);
            }
        }

        /// <summary>
        /// UI ウインドウの表示状態を取得（BlankMapAdd 等の外部プラグイン連携用）。
        /// </summary>
        public static bool TryGetUiVisible(out bool visible)
        {
            visible = false;
            Plugin inst = _instance;
            if (inst == null || inst._settings == null) return false;
            visible = inst._settings.UiVisible;
            return true;
        }

        /// <summary>
        /// UI ウインドウの表示状態を設定。ConfigEntry も同期。
        /// </summary>
        public static bool TrySetUiVisible(bool visible)
        {
            Plugin inst = _instance;
            if (inst == null || inst._settings == null) return false;
            inst._settings.UiVisible = visible;
            if (inst._cfgUiVisible != null && inst._cfgUiVisible.Value != visible)
            {
                inst._cfgUiVisible.Value = visible;
            }
            inst.SaveSettings();
            return true;
        }
    }
}
