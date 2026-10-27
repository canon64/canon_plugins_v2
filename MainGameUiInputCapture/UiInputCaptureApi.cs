using System.Collections.Generic;
using UnityEngine;

namespace MainGameUiInputCapture
{
    public static class UiInputCaptureApi
    {
        public static bool IsAvailable => Plugin.Instance != null;

        public static void Sync(string ownerKey, string sourceKey, bool active)
        {
            Plugin.Instance?.Runtime.Sync(ownerKey, sourceKey, active);
        }

        public static bool Begin(string ownerKey, string sourceKey)
        {
            return Plugin.Instance != null && Plugin.Instance.Runtime.Begin(ownerKey, sourceKey);
        }

        public static bool Tick(string ownerKey, string sourceKey)
        {
            return Plugin.Instance != null && Plugin.Instance.Runtime.Tick(ownerKey, sourceKey);
        }

        public static bool End(string ownerKey, string sourceKey)
        {
            return Plugin.Instance != null && Plugin.Instance.Runtime.End(ownerKey, sourceKey);
        }

        public static int EndOwner(string ownerKey)
        {
            return Plugin.Instance == null ? 0 : Plugin.Instance.Runtime.EndOwner(ownerKey);
        }

        public static bool SetIdleCursorUnlock(string ownerKey, bool enabled)
        {
            return Plugin.Instance != null && Plugin.Instance.Runtime.SetIdleCursorUnlock(ownerKey, enabled);
        }

        /// <summary>
        /// owner の uGUI ブロック矩形群を置き換える。矩形は IMGUI 座標（左上原点）。
        /// IMGUI ウィンドウを表示している間これを呼ぶと、その範囲のゲーム uGUI クリックを吸収する。
        /// </summary>
        public static void SetBlockRects(string ownerKey, IList<Rect> rects)
        {
            Plugin.Instance?.Runtime.SetBlockRects(ownerKey, rects);
        }

        /// <summary>owner の uGUI ブロックを解除する。</summary>
        public static void ClearBlockRects(string ownerKey)
        {
            Plugin.Instance?.Runtime.SetBlockRects(ownerKey, null);
        }

        /// <summary>
        /// owner の入力ブロック矩形群を置き換える。矩形は IMGUI 座標（左上原点）。
        /// マウスが矩形内にある間、ゲーム本編のカメラ/キー系入力を抑止し、uGUIクリックも吸収する。
        /// </summary>
        public static void SetInputBlockRects(string ownerKey, IList<Rect> rects)
        {
            Plugin.Instance?.Runtime.SetInputBlockRects(ownerKey, rects);
        }

        /// <summary>owner の入力ブロックを解除する。</summary>
        public static void ClearInputBlockRects(string ownerKey)
        {
            Plugin.Instance?.Runtime.SetInputBlockRects(ownerKey, null);
        }

        /// <summary>
        /// 現在のマウス位置が登録済み入力ブロック矩形内にあるかを返す。
        /// </summary>
        public static bool IsMouseInputBlocked()
        {
            return Plugin.Instance != null && Plugin.Instance.Runtime.IsMouseInputBlocked();
        }

        public static bool IsPointerInputBlocked()
        {
            return IsMouseInputBlocked();
        }

        /// <summary>
        /// 指定 owner 自身の入力ブロック矩形は通し、それ以外の UI 矩形内なら true を返す。
        /// UI 側が自分の操作を読む場合に使う。
        /// </summary>
        public static bool IsMouseInputBlockedForOwner(string ownerKey)
        {
            return Plugin.Instance != null && Plugin.Instance.Runtime.IsMouseInputBlockedForOwner(ownerKey);
        }

        public static bool IsPointerInputBlockedForOwner(string ownerKey)
        {
            return IsMouseInputBlockedForOwner(ownerKey);
        }

        /// <summary>
        /// Input.GetAxis の共通ラッパ。UI 入力ブロック中のマウス軸は 0 にする。
        /// </summary>
        public static float GetAxis(string axisName)
        {
            if (IsMouseAxis(axisName) && IsMouseInputBlocked())
            {
                return 0f;
            }

            return Input.GetAxis(axisName);
        }

        /// <summary>
        /// 指定 owner 自身の UI 操作として Input.GetAxis を読む。
        /// owner 以外の UI 入力ブロック中のマウス軸は 0 にする。
        /// </summary>
        public static float GetAxisForOwner(string ownerKey, string axisName)
        {
            if (IsMouseAxis(axisName) && IsMouseInputBlockedForOwner(ownerKey))
            {
                return 0f;
            }

            return Input.GetAxis(axisName);
        }

        /// <summary>
        /// Input.GetAxisRaw の共通ラッパ。UI 入力ブロック中のマウス軸は 0 にする。
        /// </summary>
        public static float GetAxisRaw(string axisName)
        {
            if (IsMouseAxis(axisName) && IsMouseInputBlocked())
            {
                return 0f;
            }

            return Input.GetAxisRaw(axisName);
        }

        /// <summary>
        /// 指定 owner 自身の UI 操作として Input.GetAxisRaw を読む。
        /// owner 以外の UI 入力ブロック中のマウス軸は 0 にする。
        /// </summary>
        public static float GetAxisRawForOwner(string ownerKey, string axisName)
        {
            if (IsMouseAxis(axisName) && IsMouseInputBlockedForOwner(ownerKey))
            {
                return 0f;
            }

            return Input.GetAxisRaw(axisName);
        }

        public static bool GetMouseButton(int button)
        {
            return !IsMouseInputBlocked() && Input.GetMouseButton(button);
        }

        public static bool GetMouseButtonForOwner(string ownerKey, int button)
        {
            return !IsMouseInputBlockedForOwner(ownerKey) && Input.GetMouseButton(button);
        }

        public static bool GetMouseButtonDown(int button)
        {
            return !IsMouseInputBlocked() && Input.GetMouseButtonDown(button);
        }

        public static bool GetMouseButtonDownForOwner(string ownerKey, int button)
        {
            return !IsMouseInputBlockedForOwner(ownerKey) && Input.GetMouseButtonDown(button);
        }

        public static bool GetMouseButtonUp(int button)
        {
            return !IsMouseInputBlocked() && Input.GetMouseButtonUp(button);
        }

        public static bool GetMouseButtonUpForOwner(string ownerKey, int button)
        {
            return !IsMouseInputBlockedForOwner(ownerKey) && Input.GetMouseButtonUp(button);
        }

        public static Vector2 GetMouseScrollDelta()
        {
            return IsMouseInputBlocked() ? Vector2.zero : Input.mouseScrollDelta;
        }

        public static Vector2 GetMouseScrollDeltaForOwner(string ownerKey)
        {
            return IsMouseInputBlockedForOwner(ownerKey) ? Vector2.zero : Input.mouseScrollDelta;
        }

        /// <summary>
        /// UI 入力ブロックに対応した Mouse ScrollWheel 値を返す。
        /// </summary>
        public static float GetMouseScrollWheel()
        {
            return GetAxis("Mouse ScrollWheel");
        }

        /// <summary>
        /// 指定 owner 自身の UI 操作として Mouse ScrollWheel 値を返す。
        /// </summary>
        public static float GetMouseScrollWheelForOwner(string ownerKey)
        {
            return GetAxisForOwner(ownerKey, "Mouse ScrollWheel");
        }

        private static bool IsMouseAxis(string axisName)
        {
            return string.Equals(axisName, "Mouse ScrollWheel", System.StringComparison.Ordinal)
                || string.Equals(axisName, "Mouse X", System.StringComparison.Ordinal)
                || string.Equals(axisName, "Mouse Y", System.StringComparison.Ordinal);
        }

        public static bool IsOwnerActive(string ownerKey)
        {
            return Plugin.Instance != null && Plugin.Instance.Runtime.IsOwnerActive(ownerKey);
        }

        public static void SetOwnerDebug(string ownerKey, bool enabled)
        {
            Plugin.Instance?.SetOwnerDebug(ownerKey, enabled);
        }

        public static bool IsAnyActive()
        {
            return Plugin.Instance != null && Plugin.Instance.Runtime.IsAnyActive;
        }

        public static string GetStateSummary()
        {
            return Plugin.Instance == null ? "plugin-unavailable" : Plugin.Instance.Runtime.GetStateSummary();
        }
    }
}
