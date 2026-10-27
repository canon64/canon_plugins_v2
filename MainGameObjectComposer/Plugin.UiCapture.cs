using System.Collections.Generic;
using MainGameUiInputCapture;
using UnityEngine;

namespace MainGameObjectComposer
{
    /// <summary>
    /// FOVSlider と同じパターンで MainGameUiInputCapture プラグインを利用する。
    /// 自前でカーソル制御は一切行わない。すべてプラグイン側に任せる。
    /// </summary>
    public sealed partial class Plugin
    {
        private const string UiCaptureOwner = Guid + ".input";
        private const string UiCaptureSourceWindow = "window";
        private const string UiCaptureSourceSlider = "slider";
        private const string UiCaptureSourceHeader = "window-header";
        private const string UiCaptureSourceGizmo = "gizmo-drag";
        private const float WindowHeaderHeight = 24f;

        private Rect _lastMainWindowRect;
        private bool _gizmoDragging;
        private readonly List<Rect> _blockRectScratch = new List<Rect>(1);

        // Gizmo 側から呼ばれる
        private void NotifyGizmoDragState(bool dragging)
        {
            _gizmoDragging = dragging;
        }

        // 古いコードからの参照を維持するための薄いラッパ
        private void UpdateUiCaptureState()
        {
            UpdateUiInputCapture();
        }

        // OnGUI 内のヘッダードラッグ検出用（薄いまま残す）
        private void UpdateWindowDragHint(Rect dragRect, ref bool draggingFlag)
        {
            Event ev = Event.current;
            if (ev == null) return;

            if (ev.type == EventType.MouseDown && ev.button == 0 && dragRect.Contains(ev.mousePosition))
            {
                draggingFlag = true;
            }
            else if (ev.type == EventType.MouseUp && ev.button == 0)
            {
                draggingFlag = false;
            }
            else if (!GetOwnerMouseButton(0))
            {
                draggingFlag = false;
            }
        }

        /// <summary>
        /// Update() から毎フレーム呼ぶ。FOVSlider と同じ Sync ベースの実装。
        /// </summary>
        private void UpdateUiInputCapture()
        {
            if (!UiInputCaptureApi.IsAvailable) return;
            if (_settings == null) return;

            bool visible = _settings.UiVisible && !IsHostedByUnifiedUi();
            // ウインドウを開いている間はカーソルを常時解放しておく（操作していなくても）
            UiInputCaptureApi.SetIdleCursorUnlock(UiCaptureOwner, visible);

            bool mouseInWindow = visible && IsMouseInGuiRect(_lastMainWindowRect);
            bool mouseDown = GetOwnerMouseButton(0);
            bool hotControlActive = GUIUtility.hotControl != 0;

            bool sliderActive = visible && mouseDown && hotControlActive && mouseInWindow;
            bool headerActive = visible && mouseDown && IsMouseInGuiRect(
                new Rect(_lastMainWindowRect.x, _lastMainWindowRect.y, _lastMainWindowRect.width, WindowHeaderHeight));
            bool windowActive = visible && (mouseInWindow || sliderActive || headerActive);

            UiInputCaptureApi.Sync(UiCaptureOwner, UiCaptureSourceWindow, windowActive);
            UiInputCaptureApi.Sync(UiCaptureOwner, UiCaptureSourceSlider, sliderActive);
            UiInputCaptureApi.Sync(UiCaptureOwner, UiCaptureSourceHeader, headerActive);
            UiInputCaptureApi.Sync(UiCaptureOwner, UiCaptureSourceGizmo, _gizmoDragging);

            // uGUI クリック貫通防止: 表示中のウィンドウ範囲を共有ブロッカーに登録する。
            // （F7 状態ウィンドウは現状未描画なので main のみ。複数になればここに足す）
            if (visible && _lastMainWindowRect.width > 0f && _lastMainWindowRect.height > 0f)
            {
                _blockRectScratch.Clear();
                _blockRectScratch.Add(_lastMainWindowRect);
                UiInputCaptureApi.SetInputBlockRects(UiCaptureOwner, _blockRectScratch);
            }
            else
            {
                UiInputCaptureApi.ClearInputBlockRects(UiCaptureOwner);
            }
        }

        // OnDestroy から呼ばれる。プラグインに登録した全ソースを片付ける
        private void ReleaseExternalUiCapture(string reason)
        {
            if (UiInputCaptureApi.IsAvailable)
            {
                UiInputCaptureApi.SetIdleCursorUnlock(UiCaptureOwner, false);
                UiInputCaptureApi.ClearInputBlockRects(UiCaptureOwner);
                UiInputCaptureApi.EndOwner(UiCaptureOwner);
            }
        }

        // OnDestroy から呼ばれる（旧API互換）。実体は ReleaseExternalUiCapture と同じ
        private void StopUiCapture(string reason)
        {
            ReleaseExternalUiCapture(reason);
        }

        private static bool IsMouseInGuiRect(Rect guiRect)
        {
            if (guiRect.width <= 0f || guiRect.height <= 0f) return false;
            Vector3 mouse = Input.mousePosition;
            Vector2 guiMouse = new Vector2(mouse.x, Screen.height - mouse.y);
            return guiRect.Contains(guiMouse);
        }

        private bool GetOwnerMouseButton(int button)
        {
            // The embedded Composer tab is inside UnifiedUi's input block rect.
            // Composer's own owner is intentionally inactive there, so its
            // owner-filtered mouse read would reject every slider drag.
            if (IsHostedByUnifiedUi()) return Input.GetMouseButton(button);
            return UiInputCaptureApi.GetMouseButtonForOwner(UiCaptureOwner, button);
        }

        private static bool GetGameMouseButtonDown(int button)
        {
            return UiInputCaptureApi.GetMouseButtonDown(button);
        }
    }
}
