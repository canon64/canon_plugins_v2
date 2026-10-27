using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MainGameSubtitleCore
{
    /// <summary>
    /// 入力パネルをドラッグで動かす。
    ///
    /// パネルは UnityUI(RectTransform) で組まれているので、IMGUI の GUI.DragWindow は使えない。
    /// EventSystem のドラッグイベントを拾って anchoredPosition を動かす。
    ///
    /// 動かした位置は都度コールバックへ渡し、呼び出し側が設定へ保存する。
    /// 画面外へ出ると掴めなくなるので、常に画面内へ収める。
    /// </summary>
    internal sealed class InputPanelDragger : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private RectTransform _target;
        private Canvas _canvas;
        private Vector2 _pointerStart;
        private Vector2 _panelStart;

        internal Action<Vector2> OnMoving;   // ドラッグ中、毎フレーム
        internal Action<Vector2> OnMoved;    // 離した時
        internal Func<bool> IsEnabled;

        // ── 入力調停 ────────────────────────────────────────────
        // Hシーン中はゲーム側がカーソルを毎フレーム固定し直すので、そのままでは
        // カーソルが震えて動かず掴めない。MainGameUiInputCapture へ
        // 「今UIを操作中だ」と申告して、その間だけ固定を外してもらう。
        // DLL参照はせずリフレクションで引く（未導入でも落ちない）。
        private const string OwnerKey = "MainGameSubtitleCore";
        private const string SourceKey = "input-panel-drag";
        private static bool _apiResolved;
        private bool _loggedDown, _loggedDrag;
        private static MethodInfo _begin, _tick, _end;

        private static void ResolveApi()
        {
            if (_apiResolved) return;
            _apiResolved = true;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type t = asm.GetType("MainGameUiInputCapture.UiInputCaptureApi", false);
                    if (t == null) continue;
                    var sig = new[] { typeof(string), typeof(string) };
                    _begin = t.GetMethod("Begin", BindingFlags.Public | BindingFlags.Static, null, sig, null);
                    _tick  = t.GetMethod("Tick",  BindingFlags.Public | BindingFlags.Static, null, sig, null);
                    _end   = t.GetMethod("End",   BindingFlags.Public | BindingFlags.Static, null, sig, null);
                    break;
                }
                catch { }
            }
        }

        private static void Capture(MethodInfo m)
        {
            if (m == null) return;
            try { m.Invoke(null, new object[] { OwnerKey, SourceKey }); }
            catch { }
        }

        internal void Bind(RectTransform target, Canvas canvas)
        {
            _target = target;
            _canvas = canvas;
        }

        private bool Usable => _target != null && (IsEnabled == null || IsEnabled());

        /// <summary>
        /// 押した瞬間に入力を掴む。
        /// Hシーン中はゲームがカーソルをロックしていて Input.mousePosition が動かない。
        /// するとEventSystemがドラッグ開始と判定せず OnBeginDrag が永久に呼ばれない。
        /// 解除の申告を OnBeginDrag に置くと、呼ばれない場所に置くことになる。
        /// 移動を必要としない OnPointerDown で先に掴む。
        /// </summary>
        public void OnPointerDown(PointerEventData e)
        {
            if (!_loggedDown)
            {
                _loggedDown = true;
                Plugin.LogStatic($"[drag] 押された usable={Usable} pos={e.position}");
            }
            if (!Usable) return;
            ResolveApi();
            Capture(_begin);
            // ロック解除が効く前でも基準点を取れるようにしておく
            _panelStart = _target.anchoredPosition;
            _pointerStart = ToCanvasPoint(e);
        }

        public void OnPointerUp(PointerEventData e)
        {
            Capture(_end);
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (!Usable) return;
            ResolveApi();
            Capture(_begin);
            _panelStart = _target.anchoredPosition;
            _pointerStart = ToCanvasPoint(e);
        }

        public void OnDrag(PointerEventData e)
        {
            if (!Usable) return;
            Capture(_tick);   // 掴んでいる間は毎フレーム申告し続ける
            Vector2 delta = ToCanvasPoint(e) - _pointerStart;
            if (!_loggedDrag)
            {
                _loggedDrag = true;
                Plugin.LogStatic($"[drag] 移動開始 delta={delta}");
            }
            Vector2 next = ClampToScreen(_panelStart + delta);
            _target.anchoredPosition = next;
            OnMoving?.Invoke(next);   // レイアウトに戻される前に保持させる
        }

        public void OnEndDrag(PointerEventData e)
        {
            Capture(_end);
            if (!Usable) return;
            OnMoved?.Invoke(_target.anchoredPosition);
        }

        private void OnDisable()
        {
            // 掴んだまま無効化されると入力が戻らないので必ず離す。
            Capture(_end);
        }

        private Vector2 ToCanvasPoint(PointerEventData e)
        {
            RectTransform canvasRect = _canvas != null ? _canvas.transform as RectTransform : null;
            if (canvasRect == null)
                return e.position;

            Vector2 local;
            Camera cam = (_canvas != null && _canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                ? null
                : (_canvas != null ? _canvas.worldCamera : null);
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, e.position, cam, out local) ? local : (Vector2)e.position;
        }

        /// <summary>掴めなくならないよう、パネルの一部が必ず画面内に残るようにする。</summary>
        private Vector2 ClampToScreen(Vector2 pos)
        {
            return ClampToCanvas(pos, _target, _canvas);
        }

        internal static Vector2 ClampToCanvas(Vector2 pos, RectTransform target, Canvas canvas)
        {
            RectTransform canvasRect = canvas != null ? canvas.transform as RectTransform : null;
            if (canvasRect == null || target == null || canvasRect.rect.width <= 0f || canvasRect.rect.height <= 0f)
                return pos;

            float halfW = canvasRect.rect.width * 0.5f;
            float halfH = canvasRect.rect.height * 0.5f;
            // Keep the complete panel visible, including its drag handle.
            const float margin = 8f;
            float limitX = Mathf.Max(0f, halfW - target.rect.width * 0.5f - margin);
            float limitY = Mathf.Max(0f, halfH - target.rect.height * 0.5f - margin);

            pos.x = Mathf.Clamp(pos.x, -limitX, limitX);
            pos.y = Mathf.Clamp(pos.y, -limitY, limitY);
            return pos;
        }
    }
}
