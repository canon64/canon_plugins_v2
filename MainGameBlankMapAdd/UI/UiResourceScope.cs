using System;
using UnityEngine;
using UnityEngine.UI;
using MainGameUiInputCapture;

namespace MainGameBlankMapAdd.UI
{
    internal sealed class UiResourceScope : IDisposable
    {
        private GameObject _blockerRoot;
        private readonly System.Collections.Generic.List<RectTransform> _blockerRects = new System.Collections.Generic.List<RectTransform>();
        private Texture2D _blackTexture;
        internal bool UsesSharedInputCapture => UiInputCaptureApi.IsAvailable;
        internal void SyncPlaybackCursor(bool enabled)
        {
            UiInputCaptureApi.SetIdleCursorUnlock(InputOwner, enabled);
        }
        private System.Collections.Generic.IList<Rect> _inputRects;
        private const string InputOwner = "MainGameBlankMapAdd.PlaybackBar";

        private void RegisterInputBlock(bool visible)
        {
            if (visible) UiInputCaptureApi.SetInputBlockRects(InputOwner, _inputRects);
            else UiInputCaptureApi.ClearInputBlockRects(InputOwner);
        }

        internal Texture2D BlackTexture
        {
            get
            {
                if (_blackTexture != null) return _blackTexture;
                _blackTexture = new Texture2D(1, 1);
                _blackTexture.SetPixel(0, 0, Color.black);
                _blackTexture.Apply();
                return _blackTexture;
            }
        }

        internal void UpdateBlocker(System.Collections.Generic.IList<Rect> rects)
        {
            _inputRects = rects;
            RegisterInputBlock(true);
            EnsureBlocker();
            while (_blockerRects.Count < rects.Count) {
                var imageObject = new GameObject("BlockerImage");
                imageObject.transform.SetParent(_blockerRoot.transform, false);
                var image = imageObject.AddComponent<Image>();
                image.color = Color.clear;
                image.raycastTarget = true;
                _blockerRects.Add(imageObject.GetComponent<RectTransform>());
            }
            for (int i = 0; i < _blockerRects.Count; i++) {
                var target = _blockerRects[i];
                bool active = i < rects.Count && rects[i].width > 0 && rects[i].height > 0;
                target.gameObject.SetActive(active);
                if (!active) continue;
                var rect = rects[i];
                target.anchorMin = new Vector2(rect.x / Screen.width, (Screen.height - rect.yMax) / Screen.height);
                target.anchorMax = new Vector2(rect.xMax / Screen.width, (Screen.height - rect.y) / Screen.height);
                target.offsetMin = target.offsetMax = Vector2.zero;
            }
            if (!_blockerRoot.activeSelf) _blockerRoot.SetActive(true);
        }

        internal void HideBlocker()
        {
            RegisterInputBlock(false);
            if (_blockerRoot != null && _blockerRoot.activeSelf) _blockerRoot.SetActive(false);
        }

        private void EnsureBlocker()
        {
            if (_blockerRoot != null) return;
            _blockerRoot = new GameObject("PlaybackBarBlocker");
            UnityEngine.Object.DontDestroyOnLoad(_blockerRoot);

            Canvas canvas = _blockerRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 29999;
            _blockerRoot.AddComponent<GraphicRaycaster>();

        }

        public void Dispose()
        {
            RegisterInputBlock(false);
            UiInputCaptureApi.SetIdleCursorUnlock(InputOwner, false);
            if (_blockerRoot != null) UnityEngine.Object.Destroy(_blockerRoot);
            if (_blackTexture != null) UnityEngine.Object.Destroy(_blackTexture);
            _blockerRoot = null;
            _blockerRects.Clear();
            _blackTexture = null;
        }
    }
}
