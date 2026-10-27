using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine.UI;

namespace MainGameAlbumAspect
{
    /// <summary>
    /// アルバム関連クラスへの Harmony パッチ。
    ///
    /// 対象（逆コンパイル済みの事実）:
    ///  - CapFileViewer.Preview（private nested class） … public Texture capture の setter が
    ///    _rawCapImage.texture へ代入する唯一の場所。縦横比調整が無いので引き伸ばされる。
    ///  - CapFileNode.LoadTexture(string)                … サムネイル用 RawImage(img) へ非同期でテクスチャ代入。
    ///  - CapFileDeleteDialog.SetCap(Texture)            … 削除確認ダイアログの RawImage(Cap) へ代入。
    ///
    /// いずれも「テクスチャが入る瞬間」を捕まえて AspectFitDriver を貼るだけ。
    /// 実際の縦横比補正は AspectFitDriver が毎 LateUpdate で行う（レイアウト確定のタイミング問題を避けるため）。
    /// </summary>
    internal static class AlbumAspectPatches
    {
        private static FieldInfo _previewRawCapImageField;
        private static FieldInfo _capFileNodeImgField;
        private static FieldInfo _deleteDialogCapField;

        private static FieldInfo _viewerPreviewField;
        private static FieldInfo _viewerCapsField;
        private static FieldInfo _viewerDelDialogField;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null) return;

            PatchPreview(harmony);
            PatchThumbnail(harmony);
            PatchDeleteDialog(harmony);

            // 上の 3 つは「テクスチャ代入の瞬間」を捕まえる経路だが、
            // Preview.capture の setter や SetCap は本体が 1 行しかなく Mono にインライン化されると
            // パッチが発火しないことがある。そこで CapFileViewer 側の大きめのメソッドからも
            // 同じ RawImage 群にドライバを貼れるようにしておく（Attach は冪等）。
            PatchViewerAttachPoints(harmony);
        }

        // ---------------------------------------------------------------
        // CapFileViewer.Preview.capture (setter)
        // ---------------------------------------------------------------
        private static void PatchPreview(Harmony harmony)
        {
            try
            {
                Type viewerType = AccessTools.TypeByName("CapFileViewer");
                if (viewerType == null)
                {
                    Plugin.SafeWarn("CapFileViewer 型が見つからない。プレビューのパッチを見送る。");
                    return;
                }

                Type previewType = AccessTools.Inner(viewerType, "Preview");
                if (previewType == null)
                {
                    Plugin.SafeWarn("CapFileViewer+Preview 型が見つからない。プレビューのパッチを見送る。");
                    return;
                }

                _previewRawCapImageField = AccessTools.Field(previewType, "_rawCapImage");
                if (_previewRawCapImageField == null)
                {
                    Plugin.SafeWarn("Preview._rawCapImage フィールドが見つからない。プレビューのパッチを見送る。");
                    return;
                }

                MethodInfo setter = AccessTools.PropertySetter(previewType, "capture");
                if (setter == null)
                {
                    Plugin.SafeWarn("Preview.capture の setter が見つからない。プレビューのパッチを見送る。");
                    return;
                }

                harmony.Patch(setter, postfix: MakePostfix(nameof(PreviewCapturePostfix)));
                Plugin.Instance?.LogInfo("patched: CapFileViewer+Preview.capture (setter)");
            }
            catch (Exception e)
            {
                Plugin.SafeWarn("プレビューのパッチで例外: " + e);
            }
        }

        private static void PreviewCapturePostfix(object __instance)
        {
            try
            {
                if (__instance == null || _previewRawCapImageField == null) return;

                var rawImage = _previewRawCapImageField.GetValue(__instance) as RawImage;
                if (rawImage == null) return;

                AspectFitDriver.Attach(rawImage, AspectTargetKind.Preview);
            }
            catch (Exception e)
            {
                Plugin.SafeWarn("PreviewCapturePostfix で例外: " + e.Message);
            }
        }

        // ---------------------------------------------------------------
        // CapFileNode.LoadTexture(string)
        // ---------------------------------------------------------------
        private static void PatchThumbnail(Harmony harmony)
        {
            try
            {
                Type nodeType = AccessTools.TypeByName("CapFileNode");
                if (nodeType == null)
                {
                    Plugin.SafeWarn("CapFileNode 型が見つからない。サムネイルのパッチを見送る。");
                    return;
                }

                _capFileNodeImgField = AccessTools.Field(nodeType, "img");
                if (_capFileNodeImgField == null)
                {
                    Plugin.SafeWarn("CapFileNode.img フィールドが見つからない。サムネイルのパッチを見送る。");
                    return;
                }

                MethodInfo loadTexture = AccessTools.Method(nodeType, "LoadTexture", new[] { typeof(string) });
                if (loadTexture == null)
                {
                    Plugin.SafeWarn("CapFileNode.LoadTexture(string) が見つからない。サムネイルのパッチを見送る。");
                    return;
                }

                harmony.Patch(loadTexture, postfix: MakePostfix(nameof(CapFileNodeLoadTexturePostfix)));
                Plugin.Instance?.LogInfo("patched: CapFileNode.LoadTexture(string)");
            }
            catch (Exception e)
            {
                Plugin.SafeWarn("サムネイルのパッチで例外: " + e);
            }
        }

        private static void CapFileNodeLoadTexturePostfix(object __instance)
        {
            try
            {
                if (__instance == null || _capFileNodeImgField == null) return;

                var rawImage = _capFileNodeImgField.GetValue(__instance) as RawImage;
                if (rawImage == null) return;

                // LoadTexture は非同期でテクスチャを差し込むので、ここではドライバを貼るだけでよい。
                // 実際の補正はドライバが texture の到着を検知して行う。
                AspectFitDriver.Attach(rawImage, AspectTargetKind.Thumbnail);
            }
            catch (Exception e)
            {
                Plugin.SafeWarn("CapFileNodeLoadTexturePostfix で例外: " + e.Message);
            }
        }

        // ---------------------------------------------------------------
        // CapFileDeleteDialog.SetCap(Texture)
        // ---------------------------------------------------------------
        private static void PatchDeleteDialog(Harmony harmony)
        {
            try
            {
                Type dialogType = AccessTools.TypeByName("CapFileDeleteDialog");
                if (dialogType == null)
                {
                    Plugin.SafeWarn("CapFileDeleteDialog 型が見つからない。削除ダイアログのパッチを見送る。");
                    return;
                }

                _deleteDialogCapField = AccessTools.Field(dialogType, "Cap");
                if (_deleteDialogCapField == null)
                {
                    Plugin.SafeWarn("CapFileDeleteDialog.Cap フィールドが見つからない。削除ダイアログのパッチを見送る。");
                    return;
                }

                MethodInfo setCap = AccessTools.Method(dialogType, "SetCap");
                if (setCap == null)
                {
                    Plugin.SafeWarn("CapFileDeleteDialog.SetCap が見つからない。削除ダイアログのパッチを見送る。");
                    return;
                }

                harmony.Patch(setCap, postfix: MakePostfix(nameof(DeleteDialogSetCapPostfix)));
                Plugin.Instance?.LogInfo("patched: CapFileDeleteDialog.SetCap");
            }
            catch (Exception e)
            {
                Plugin.SafeWarn("削除ダイアログのパッチで例外: " + e);
            }
        }

        private static void DeleteDialogSetCapPostfix(object __instance)
        {
            try
            {
                if (__instance == null || _deleteDialogCapField == null) return;

                var rawImage = _deleteDialogCapField.GetValue(__instance) as RawImage;
                if (rawImage == null) return;

                AspectFitDriver.Attach(rawImage, AspectTargetKind.DeleteDialog);
            }
            catch (Exception e)
            {
                Plugin.SafeWarn("DeleteDialogSetCapPostfix で例外: " + e.Message);
            }
        }

        // ---------------------------------------------------------------
        // CapFileViewer 側の確実な取り付け点
        //   Initialize()  … シングルトン初期化時に一度だけ
        //   SetPage(int)  … ページ表示のたび（保険）
        // どちらも本体が大きいのでインライン化されない。
        // ---------------------------------------------------------------
        private static void PatchViewerAttachPoints(Harmony harmony)
        {
            try
            {
                Type viewerType = AccessTools.TypeByName("CapFileViewer");
                if (viewerType == null) return;

                _viewerPreviewField = AccessTools.Field(viewerType, "_preview");
                _viewerCapsField = AccessTools.Field(viewerType, "_caps");
                _viewerDelDialogField = AccessTools.Field(viewerType, "_delDialog");

                // 各 Patch*() がメソッド未発見で早期 return していた場合に備えて、
                // RawImage を持つフィールドはここでも独立に解決しておく。
                if (_previewRawCapImageField == null)
                {
                    Type previewType = AccessTools.Inner(viewerType, "Preview");
                    if (previewType != null)
                    {
                        _previewRawCapImageField = AccessTools.Field(previewType, "_rawCapImage");
                    }
                }

                if (_capFileNodeImgField == null)
                {
                    Type nodeType = AccessTools.TypeByName("CapFileNode");
                    if (nodeType != null)
                    {
                        _capFileNodeImgField = AccessTools.Field(nodeType, "img");
                    }
                }

                if (_deleteDialogCapField == null)
                {
                    Type dialogType = AccessTools.TypeByName("CapFileDeleteDialog");
                    if (dialogType != null)
                    {
                        _deleteDialogCapField = AccessTools.Field(dialogType, "Cap");
                    }
                }

                if (_viewerPreviewField == null && _viewerCapsField == null && _viewerDelDialogField == null)
                {
                    Plugin.SafeWarn("CapFileViewer の _preview/_caps/_delDialog がどれも見つからない。取り付け点のパッチを見送る。");
                    return;
                }

                MethodInfo initialize = AccessTools.Method(viewerType, "Initialize");
                if (initialize != null)
                {
                    harmony.Patch(initialize, postfix: MakePostfix(nameof(ViewerAttachPostfix)));
                    Plugin.Instance?.LogInfo("patched: CapFileViewer.Initialize");
                }

                MethodInfo setPage = AccessTools.Method(viewerType, "SetPage", new[] { typeof(int) });
                if (setPage != null)
                {
                    harmony.Patch(setPage, postfix: MakePostfix(nameof(ViewerAttachPostfix)));
                    Plugin.Instance?.LogInfo("patched: CapFileViewer.SetPage(int)");
                }
            }
            catch (Exception e)
            {
                Plugin.SafeWarn("CapFileViewer の取り付け点パッチで例外: " + e);
            }
        }

        private static void ViewerAttachPostfix(object __instance)
        {
            try
            {
                if (__instance == null) return;

                // プレビュー
                if (_viewerPreviewField != null && _previewRawCapImageField != null)
                {
                    object preview = _viewerPreviewField.GetValue(__instance);
                    if (preview != null)
                    {
                        var rawImage = _previewRawCapImageField.GetValue(preview) as RawImage;
                        if (rawImage != null)
                        {
                            AspectFitDriver.Attach(rawImage, AspectTargetKind.Preview);
                        }
                    }
                }

                // サムネイル一覧
                if (_viewerCapsField != null && _capFileNodeImgField != null)
                {
                    var caps = _viewerCapsField.GetValue(__instance) as Array;
                    if (caps != null)
                    {
                        for (int i = 0; i < caps.Length; i++)
                        {
                            object node = caps.GetValue(i);
                            if (node == null) continue;

                            var rawImage = _capFileNodeImgField.GetValue(node) as RawImage;
                            if (rawImage != null)
                            {
                                AspectFitDriver.Attach(rawImage, AspectTargetKind.Thumbnail);
                            }
                        }
                    }
                }

                // 削除確認ダイアログ
                if (_viewerDelDialogField != null && _deleteDialogCapField != null)
                {
                    object dialog = _viewerDelDialogField.GetValue(__instance);
                    if (dialog != null)
                    {
                        var rawImage = _deleteDialogCapField.GetValue(dialog) as RawImage;
                        if (rawImage != null)
                        {
                            AspectFitDriver.Attach(rawImage, AspectTargetKind.DeleteDialog);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.SafeWarn("ViewerAttachPostfix で例外: " + e.Message);
            }
        }

        // ---------------------------------------------------------------
        private static HarmonyMethod MakePostfix(string methodName)
        {
            MethodInfo m = AccessTools.Method(typeof(AlbumAspectPatches), methodName);
            if (m == null)
            {
                throw new MissingMethodException("AlbumAspectPatches." + methodName + " が見つからない");
            }

            return new HarmonyMethod(m);
        }
    }
}
