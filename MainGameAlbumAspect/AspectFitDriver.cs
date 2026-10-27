using System;
using UnityEngine;
using UnityEngine.UI;

namespace MainGameAlbumAspect
{
    /// <summary>
    /// アルバムの RawImage に貼り付けて、毎 LateUpdate でテクスチャの縦横比に合わせて見た目を整えるドライバ。
    ///
    /// Harmony の postfix で「テクスチャが差し込まれた瞬間」に一度だけ補正する方式だと、
    ///  - まだレイアウトが確定しておらず rect が 0 のことがある
    ///  - LayoutGroup / Canvas のリビルドで後から矩形が上書きされる
    ///  - 解像度変更やモード変更に追従できない
    /// という取りこぼしが出るため、常駐して毎フレーム「基準矩形から計算し直す」方式にしている。
    /// 基準値（sizeDelta / アンカー / uvRect）は最初に一度だけ控えるので、繰り返し適用しても値がドリフトしない。
    /// </summary>
    internal sealed class AspectFitDriver : MonoBehaviour
    {
        private const float Epsilon = 0.01f;

        internal AspectTargetKind Kind;

        private RawImage _rawImage;
        private RectTransform _rect;
        private AspectRatioFitter _fitter;
        private float _nextFitterProbeAt;

        private bool _baseCaptured;
        private Vector2 _baseSizeDelta;
        private Vector2 _baseAnchorMin;
        private Vector2 _baseAnchorMax;
        private Vector2 _baseAnchoredPos;
        private Rect _baseUvRect;

        // 親(枠)を触った時の復元用
        private RectTransform _parentRect;
        private bool _parentBaseCaptured;
        private Vector2 _parentBaseSizeDelta;
        private Vector2 _parentBaseAnchorMin;
        private Vector2 _parentBaseAnchorMax;
        private bool _parentModified;
        private bool _parentLogged;

        private bool _modified;
        private bool _layoutGroupNoticed;
        private Transform _layoutGroupParent;
        private bool _hasLayoutGroupParent;
        private float _nextLayoutGroupProbeAt;
        private bool _firstApplyLogged;
        private int _lastLoggedTexId;

        /// <summary>対象の RawImage にドライバを貼る（既にあれば使い回す）。</summary>
        internal static void Attach(RawImage rawImage, AspectTargetKind kind)
        {
            if (rawImage == null) return;

            var driver = rawImage.GetComponent<AspectFitDriver>();
            if (driver == null)
            {
                driver = rawImage.gameObject.AddComponent<AspectFitDriver>();
                Plugin.SafeDebug("AspectFitDriver attached: kind=" + kind + " go=" + rawImage.gameObject.name);
            }

            driver.Kind = kind;
            driver._rawImage = rawImage;
            driver._rect = rawImage.rectTransform;
        }

        private void LateUpdate()
        {
            try
            {
                Tick();
            }
            catch (Exception e)
            {
                Plugin.SafeWarn("AspectFitDriver.Tick で例外: " + e.Message);
                enabled = false;
            }
        }

        private void Tick()
        {
            var plugin = Plugin.Instance;
            if (plugin == null) return;

            if (_rawImage == null)
            {
                _rawImage = GetComponent<RawImage>();
                if (_rawImage == null)
                {
                    enabled = false;
                    return;
                }
            }

            if (_rect == null)
            {
                _rect = _rawImage.rectTransform;
                if (_rect == null) return;
            }

            Texture tex = _rawImage.texture;

            bool targetEnabled =
                plugin.CfgEnabled != null && plugin.CfgEnabled.Value &&
                plugin.IsTargetEnabled(Kind);

            AlbumAspectMode mode = (targetEnabled && plugin.CfgMode != null)
                ? plugin.CfgMode.Value
                : AlbumAspectMode.Off;

            // 無効 / バニラ / テクスチャ無しなら、触った跡を戻して終わり。
            if (mode == AlbumAspectMode.Off || tex == null || tex.width <= 0 || tex.height <= 0)
            {
                RestoreBase();
                return;
            }

            if (!CaptureBaseIfNeeded()) return;

            // 親に LayoutGroup があると AspectRatioFitter は効かない（レイアウトに上書きされる）ので、
            // sizeDelta 直叩きの FitRect へ退避する。
            if ((mode == AlbumAspectMode.FitInParent || mode == AlbumAspectMode.EnvelopeParent)
                && plugin.CfgAutoFallbackOnLayoutGroup != null && plugin.CfgAutoFallbackOnLayoutGroup.Value
                && HasLayoutGroupParent())
            {
                if (!_layoutGroupNoticed)
                {
                    _layoutGroupNoticed = true;
                    plugin.LogInfo("親に LayoutGroup があるため FitRect へ退避: " + DescribeTarget());
                }

                mode = AlbumAspectMode.FitRect;
            }

            float texAspect = (float)tex.width / tex.height;
            if (texAspect <= 0f || float.IsNaN(texAspect) || float.IsInfinity(texAspect))
            {
                RestoreBase();
                return;
            }

            switch (mode)
            {
                case AlbumAspectMode.FitRect:
                    ApplyFitRect(texAspect, GetFitScale(plugin), plugin);
                    break;

                case AlbumAspectMode.FitInParent:
                    ApplyFitter(AspectRatioFitter.AspectMode.FitInParent, texAspect);
                    break;

                case AlbumAspectMode.EnvelopeParent:
                    ApplyFitter(AspectRatioFitter.AspectMode.EnvelopeParent, texAspect);
                    break;

                case AlbumAspectMode.CropUv:
                    ApplyCropUv(texAspect);
                    break;

                case AlbumAspectMode.FrameToImage:
                    // まだ枠を書き換えない。親子の構造が分かっていない状態で触ると必ず崩れる。
                    // 何が居るかを一度だけ出し、安全に触れる対象を確定させてから実装する。
                    DumpHierarchyOnce(plugin, texAspect);
                    ApplyFitRect(texAspect, GetFitScale(plugin), plugin);
                    break;
            }

            LogFirstApply(mode, tex, texAspect);
        }

        private static float GetFitScale(Plugin plugin)
        {
            if (plugin.CfgFitScale == null) return 1f;
            return Mathf.Clamp(plugin.CfgFitScale.Value, 0.1f, 1f);
        }

        // ---------------------------------------------------------------
        // 基準値の記録
        // ---------------------------------------------------------------
        private bool CaptureBaseIfNeeded()
        {
            if (_baseCaptured) return true;

            Rect r = _rect.rect;
            if (r.width <= 1f || r.height <= 1f)
            {
                // まだレイアウトが走っていない。次フレームに持ち越す。
                return false;
            }

            _baseSizeDelta = _rect.sizeDelta;
            _baseAnchorMin = _rect.anchorMin;
            _baseAnchorMax = _rect.anchorMax;
            _baseAnchoredPos = _rect.anchoredPosition;
            _baseUvRect = _rawImage.uvRect;
            _baseCaptured = true;

            Plugin.SafeDebug(
                "base captured: " + DescribeTarget()
                + " rect=" + r.width.ToString("F1") + "x" + r.height.ToString("F1")
                + " sizeDelta=" + _baseSizeDelta
                + " anchors=" + _baseAnchorMin + "-" + _baseAnchorMax);

            return true;
        }

        /// <summary>基準のアンカー設定のまま置いた場合の、現在の親サイズにおける矩形サイズ。</summary>
        private Vector2 GetBaseRectSize()
        {
            Vector2 parentSize = Vector2.zero;
            var parent = _rect.parent as RectTransform;
            if (parent != null)
            {
                parentSize = parent.rect.size;
            }

            return new Vector2(
                parentSize.x * (_baseAnchorMax.x - _baseAnchorMin.x) + _baseSizeDelta.x,
                parentSize.y * (_baseAnchorMax.y - _baseAnchorMin.y) + _baseSizeDelta.y);
        }

        private bool HasLayoutGroupParent()
        {
            var parent = _rect.parent;
            if (_layoutGroupParent != parent || Time.unscaledTime >= _nextLayoutGroupProbeAt)
            {
                _layoutGroupParent = parent;
                _hasLayoutGroupParent = parent != null && parent.GetComponent<LayoutGroup>() != null;
                _nextLayoutGroupProbeAt = Time.unscaledTime + 1f;
            }
            return _hasLayoutGroupParent;
        }

        // ---------------------------------------------------------------
        // 各モードの適用
        // ---------------------------------------------------------------

        /// <summary>元の枠の内側に、縦横比を保ったまま収める（レターボックス）。</summary>
        /// <summary>
        /// 枠(親)を画像の縦横比まで詰めてから、画面の縦いっぱいへ広げる。
        /// 枠が横長のままだと、画像をいくら大きくしても両脇に白が残るため、
        /// 先に枠側を潰す必要がある。
        /// </summary>
        private void ApplyParentFrame(float texAspect, float scale, Plugin plugin)
        {
            if (_rect == null) return;
            RectTransform parent = _rect.parent as RectTransform;
            if (parent == null) return;

            if (!_parentBaseCaptured)
            {
                _parentRect = parent;
                _parentBaseSizeDelta = parent.sizeDelta;
                _parentBaseAnchorMin = parent.anchorMin;
                _parentBaseAnchorMax = parent.anchorMax;
                _parentBaseCaptured = true;
            }

            if (!_parentLogged && plugin != null)
            {
                _parentLogged = true;
                plugin.LogInfo("parent frame: name=" + parent.name
                    + " size=" + parent.rect.size
                    + " anchors=" + parent.anchorMin + "-" + parent.anchorMax
                    + " grandparent=" + (parent.parent != null ? parent.parent.name : "-"));
            }

            RectTransform grand = parent.parent as RectTransform;
            if (grand == null || grand.rect.height <= Epsilon) return;

            // 縦を基準に、画像の比で枠を作り直す。
            float targetH = grand.rect.height * Mathf.Clamp(scale, 0.1f, 1f);
            float targetW = targetH * texAspect;

            // 横がはみ出すなら横を基準に切り替える
            if (targetW > grand.rect.width)
            {
                targetW = grand.rect.width;
                targetH = targetW / texAspect;
            }

            // アンカーを中央に固定して sizeDelta で大きさを決める
            Vector2 centerAnchor = new Vector2(0.5f, 0.5f);
            Vector2 targetSize = new Vector2(targetW, targetH);
            if (parent.anchorMin != centerAnchor) parent.anchorMin = centerAnchor;
            if (parent.anchorMax != centerAnchor) parent.anchorMax = centerAnchor;
            if ((parent.sizeDelta - targetSize).sqrMagnitude > Epsilon * Epsilon)
                parent.sizeDelta = targetSize;
            _parentModified = true;
        }

        /// <summary>親(枠)を余さず埋める。枠は既に画像の比にしてあるので歪まない。</summary>
        private void ApplyFillParent()
        {
            DisableFitter();
            SetUvRect(_baseUvRect);
            if (_rect == null) return;
            if (_rect.anchorMin != Vector2.zero) _rect.anchorMin = Vector2.zero;
            if (_rect.anchorMax != Vector2.one) _rect.anchorMax = Vector2.one;
            if (_rect.offsetMin != Vector2.zero) _rect.offsetMin = Vector2.zero;
            if (_rect.offsetMax != Vector2.zero) _rect.offsetMax = Vector2.zero;
            _modified = true;
        }

        private bool _hierarchyDumped;

        /// <summary>
        /// 枠まわりの構造を一度だけ出す。書き換えは一切しない。
        /// 親・祖父・曾祖父それぞれの名前/大きさ/アンカー/付いているコンポーネントと、
        /// 親の兄弟（ページめくりの矢印などが居るはず）を並べる。
        /// これを見てから、どれを触ってよいかを決める。
        /// </summary>
        private void DumpHierarchyOnce(Plugin plugin, float texAspect)
        {
            if (_hierarchyDumped || plugin == null || _rect == null) return;
            _hierarchyDumped = true;

            plugin.LogInfo("=== 枠の構造調査 (" + DescribeTarget() + ") texAspect=" + texAspect.ToString("0.###") + " ===");

            RectTransform node = _rect;
            for (int depth = 0; depth < 4 && node != null; depth++)
            {
                string label = depth == 0 ? "self" : ("parent^" + depth);
                var comps = node.gameObject.GetComponents<Component>();
                var names = new System.Text.StringBuilder();
                for (int i = 0; i < comps.Length; i++)
                {
                    if (comps[i] == null) continue;
                    if (names.Length > 0) names.Append(",");
                    names.Append(comps[i].GetType().Name);
                }

                plugin.LogInfo(label + ": name=" + node.name
                    + " size=" + node.rect.size
                    + " sizeDelta=" + node.sizeDelta
                    + " anchor=" + node.anchorMin + "-" + node.anchorMax
                    + " pivot=" + node.pivot
                    + " pos=" + node.anchoredPosition
                    + " children=" + node.childCount
                    + " [" + names + "]");

                node = node.parent as RectTransform;
            }

            RectTransform parent = _rect.parent as RectTransform;
            RectTransform grand = parent != null ? parent.parent as RectTransform : null;
            if (grand != null)
            {
                plugin.LogInfo("-- 親の兄弟 (" + grand.name + " の子) --");
                for (int i = 0; i < grand.childCount; i++)
                {
                    Transform c = grand.GetChild(i);
                    if (c == null) continue;
                    var crt = c as RectTransform;
                    plugin.LogInfo("  [" + i + "] " + c.name
                        + (crt != null ? " size=" + crt.rect.size + " pos=" + crt.anchoredPosition : "")
                        + " active=" + c.gameObject.activeSelf);
                }
            }
            plugin.LogInfo("=== 調査ここまで ===");
        }

        private void RestoreParentFrame()
        {
            if (!_parentModified || _parentRect == null) return;
            _parentRect.anchorMin = _parentBaseAnchorMin;
            _parentRect.anchorMax = _parentBaseAnchorMax;
            _parentRect.sizeDelta = _parentBaseSizeDelta;
            _parentModified = false;
        }

        private void ApplyFitRect(float texAspect, float scale, Plugin plugin)
        {
            DisableFitter();
            RestoreAnchors();
            SetUvRect(_baseUvRect);

            // 基準の矩形。既定は画像枠そのものだが、縦長画像を縦いっぱいに広げたい時は
            // 親の矩形を基準にする（枠が小さい縦画面だと、枠基準では画像も小さくなるため）。
            Vector2 baseSize = GetBaseRectSize();
            if (plugin != null && plugin.CfgUseParentBounds != null && plugin.CfgUseParentBounds.Value)
            {
                RectTransform parent = _rect != null ? _rect.parent as RectTransform : null;
                if (parent != null && parent.rect.width > Epsilon && parent.rect.height > Epsilon)
                {
                    baseSize = new Vector2(parent.rect.width, parent.rect.height);
                }
            }

            if (baseSize.x <= Epsilon || baseSize.y <= Epsilon) return;

            float baseAspect = baseSize.x / baseSize.y;

            Vector2 target;
            if (texAspect > baseAspect)
            {
                // 画像の方が横長 → 幅を基準に高さを詰める
                target = new Vector2(baseSize.x, baseSize.x / texAspect);
            }
            else
            {
                // 画像の方が縦長 → 高さを基準に幅を詰める
                target = new Vector2(baseSize.y * texAspect, baseSize.y);
            }

            target *= scale;

            SetSizeDelta(_baseSizeDelta + (target - baseSize));
            _modified = true;
        }

        /// <summary>AspectRatioFitter に任せる（親矩形基準）。</summary>
        private void ApplyFitter(AspectRatioFitter.AspectMode aspectMode, float texAspect)
        {
            SetUvRect(_baseUvRect);

            if (_fitter == null)
            {
                _fitter = _rect.GetComponent<AspectRatioFitter>();
                if (_fitter == null)
                {
                    _fitter = _rect.gameObject.AddComponent<AspectRatioFitter>();
                }
                _nextFitterProbeAt = Time.unscaledTime + 1f;
            }

            if (!_fitter.enabled) _fitter.enabled = true;
            if (_fitter.aspectMode != aspectMode) _fitter.aspectMode = aspectMode;

            float ratio = Mathf.Clamp(texAspect, 0.001f, 1000f);
            if (Mathf.Abs(_fitter.aspectRatio - ratio) > 0.0001f)
            {
                _fitter.aspectRatio = ratio;
            }

            _modified = true;
        }

        /// <summary>uvRect の中央トリミングで埋める。レイアウトには一切触らない。</summary>
        private void ApplyCropUv(float texAspect)
        {
            DisableFitter();
            RestoreAnchors();
            SetSizeDelta(_baseSizeDelta);
            RestoreParentFrame();

            Rect r = _rect.rect;
            if (r.width <= Epsilon || r.height <= Epsilon) return;

            float rectAspect = r.width / r.height;

            float uvW = 1f;
            float uvH = 1f;
            if (texAspect > rectAspect)
            {
                uvW = rectAspect / texAspect;
            }
            else
            {
                uvH = texAspect / rectAspect;
            }

            // 元の uvRect の原点・サイズを尊重した上で中央トリミングする。
            var uv = new Rect(
                _baseUvRect.x + _baseUvRect.width * (1f - uvW) * 0.5f,
                _baseUvRect.y + _baseUvRect.height * (1f - uvH) * 0.5f,
                _baseUvRect.width * uvW,
                _baseUvRect.height * uvH);

            SetUvRect(uv);
            _modified = true;
        }

        // ---------------------------------------------------------------
        // 復元
        // ---------------------------------------------------------------
        private void RestoreBase()
        {
            if (!_modified) return;

            DisableFitter();

            if (_baseCaptured)
            {
                RestoreAnchors();
                SetSizeDelta(_baseSizeDelta);
            RestoreParentFrame();
                SetUvRect(_baseUvRect);
            }

            _modified = false;
            _firstApplyLogged = false;
        }

        private void RestoreAnchors()
        {
            if (!_baseCaptured) return;

            if (_rect.anchorMin != _baseAnchorMin) _rect.anchorMin = _baseAnchorMin;
            if (_rect.anchorMax != _baseAnchorMax) _rect.anchorMax = _baseAnchorMax;
            if ((_rect.anchoredPosition - _baseAnchoredPos).sqrMagnitude > Epsilon * Epsilon)
            {
                _rect.anchoredPosition = _baseAnchoredPos;
            }
        }

        private void DisableFitter()
        {
            if (_fitter == null && Time.unscaledTime >= _nextFitterProbeAt)
            {
                _fitter = _rect != null ? _rect.GetComponent<AspectRatioFitter>() : null;
                _nextFitterProbeAt = Time.unscaledTime + 1f;
            }

            if (_fitter != null && _fitter.enabled)
            {
                _fitter.enabled = false;
            }
        }

        // ---------------------------------------------------------------
        // 差分があるときだけ書き込む（毎フレームのレイアウト再構築を避ける）
        // ---------------------------------------------------------------
        private void SetSizeDelta(Vector2 value)
        {
            if ((_rect.sizeDelta - value).sqrMagnitude <= Epsilon * Epsilon) return;
            _rect.sizeDelta = value;
        }

        private void SetUvRect(Rect value)
        {
            Rect cur = _rawImage.uvRect;
            if (Mathf.Abs(cur.x - value.x) <= 0.0001f
                && Mathf.Abs(cur.y - value.y) <= 0.0001f
                && Mathf.Abs(cur.width - value.width) <= 0.0001f
                && Mathf.Abs(cur.height - value.height) <= 0.0001f)
            {
                return;
            }

            _rawImage.uvRect = value;
        }

        // ---------------------------------------------------------------
        private void LogFirstApply(AlbumAspectMode mode, Texture tex, float texAspect)
        {
            int texId = tex.GetInstanceID();
            if (_firstApplyLogged && texId == _lastLoggedTexId) return;

            _firstApplyLogged = true;
            _lastLoggedTexId = texId;

            Plugin.SafeDebug(
                "applied: " + DescribeTarget()
                + " mode=" + mode
                + " tex=" + tex.width + "x" + tex.height
                + " aspect=" + texAspect.ToString("F3")
                + " rect=" + _rect.rect.width.ToString("F1") + "x" + _rect.rect.height.ToString("F1"));
        }

        private string DescribeTarget()
        {
            string goName = "(null)";
            try
            {
                if (_rawImage != null) goName = _rawImage.gameObject.name;
            }
            catch
            {
                // 破棄済みなら名前は諦める。
            }

            return Kind + "/" + goName;
        }

        private void OnDestroy()
        {
            _rawImage = null;
            _rect = null;
            _fitter = null;
        }
    }
}
