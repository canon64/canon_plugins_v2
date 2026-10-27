using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace MainGameCharacterMorphBridge
{
    /// <summary>
    /// KoiSkinOverlayX(KSOX) の肌オーバーレイを変身に合わせて差し替える。
    ///
    /// 肌に上絵が乗っているキャラは、下の skinMainColor をいくら塗り直しても
    /// 上絵に隠れて見た目が変わらない（顔が典型）。上絵はテクスチャなので補間できない。
    /// そこで変身の中間点で、変身先カードに埋まっている上絵一式へ丸ごと差し替える。
    ///
    /// 実装は KSOX 自身の公開API（逆コンパイルで確認済み）だけを使う:
    ///   ExtendedSave.GetExtendedDataById(chaFile, "KSOX")  … カードから上絵データを取り出す
    ///   KoiSkinOverlayController.OverlayStorage.Load(PluginData) … 差し替え
    ///   KoiSkinOverlayController.UpdateTexture(TexType.Unknown)  … 全テクスチャ再構築
    /// KSOX が入っていない環境では何もしない。
    /// </summary>
    public sealed partial class Plugin
    {
        private const string KsoxExtDataId = "KSOX";
        private const string KsoxControllerTypeName = "KoiSkinOverlayX.KoiSkinOverlayController";

        private static bool _overlayApiResolved;
        private static MethodInfo _extGetById;          // ExtendedSave.GetExtendedDataById(ChaFile, string)
        private static Type _ksoxControllerType;
        private static Type _ksoxTexTypeEnum;

        private object _originalOverlayData;
        private object _targetOverlayData;
        private bool _overlaySwapped;
        internal bool _forceColorAfterOverlay;

        private static void ResolveOverlayApi()
        {
            if (_overlayApiResolved && _extGetById != null && _ksoxControllerType != null && _ksoxTexTypeEnum != null)
                return;

            _overlayApiResolved = true;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (_extGetById == null)
                    {
                        Type extSave = assembly.GetType("ExtensibleSaveFormat.ExtendedSave", false);
                        if (extSave != null)
                            _extGetById = extSave.GetMethod(
                                "GetExtendedDataById",
                                BindingFlags.Public | BindingFlags.Static,
                                null,
                                new[] { typeof(ChaFile), typeof(string) },
                                null);
                    }

                    if (_ksoxControllerType == null)
                    {
                        _ksoxControllerType = assembly.GetType(KsoxControllerTypeName, false);
                        if (_ksoxControllerType != null)
                            _ksoxTexTypeEnum = assembly.GetType("KoiSkinOverlayX.TexType", false);
                    }
                }
                catch
                {
                }
            }
        }

        internal bool ApplyTattooOverlays(string facePngPath, string bodyPngPath, int femaleIndex)
        {
            try { return ApplyTattooOverlayBytes(ReadTattooPng(facePngPath), ReadTattooPng(bodyPngPath), femaleIndex); }
            catch (Exception ex) { LogWarnAlways("tattoo overlay failed: " + ex); return false; }
        }

        internal bool ApplyTattooOverlayBytes(byte[] face, byte[] body, int femaleIndex)
        {
            if (!IsEnabled()) return false;
            if (!TryResolveTarget(ResolveFemaleIndex(femaleIndex), out ChaControl target, out string error))
            {
                LogWarnAlways("tattoo overlay failed: target unavailable " + error);
                return false;
            }
            if (!IsOverlaySwapAvailable())
            {
                LogWarnAlways("tattoo overlay failed: KoiSkinOverlayX unavailable");
                return false;
            }

            try
            {
                if (face == null && body == null)
                {
                    LogWarnAlways("tattoo overlay failed: face and body paths are empty");
                    return false;
                }

                Component controller = target.gameObject.GetComponent(_ksoxControllerType);
                object storage = _ksoxControllerType.GetProperty("OverlayStorage", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetValue(controller, null);
                if (storage == null)
                {
                    LogWarnAlways("tattoo overlay failed: overlay storage unavailable");
                    return false;
                }

                MethodInfo set = storage.GetType().GetMethod("SetTexture", BindingFlags.Public | BindingFlags.Instance,
                    null, new[] { _ksoxTexTypeEnum, typeof(byte[]) }, null);
                MethodInfo update = _ksoxControllerType.GetMethod("UpdateTexture", BindingFlags.Public | BindingFlags.Instance,
                    null, new[] { _ksoxTexTypeEnum }, null);
                if (set == null || update == null)
                {
                    LogWarnAlways("tattoo overlay failed: KoiSkinOverlayX API missing");
                    return false;
                }

                if (face != null)
                {
                    object faceType = Enum.Parse(_ksoxTexTypeEnum, "FaceOver");
                    set.Invoke(storage, new object[] { faceType, face });
                }
                if (body != null)
                {
                    object bodyType = Enum.Parse(_ksoxTexTypeEnum, "BodyOver");
                    set.Invoke(storage, new object[] { bodyType, body });
                }
                if (face != null) update.Invoke(controller, new[] { Enum.Parse(_ksoxTexTypeEnum, "FaceOver") });
                if (body != null) update.Invoke(controller, new[] { Enum.Parse(_ksoxTexTypeEnum, "BodyOver") });
                LogExecution("tattoo overlay applied: face=" + (face != null) + " body=" + (body != null));
                return true;
            }
            catch (Exception ex)
            {
                LogWarnAlways("tattoo overlay failed: " + ex);
                return false;
            }
        }

        internal bool ClearTattooOverlays(bool face, bool body, int femaleIndex)
        {
            if (!face && !body) return true;
            if (!IsEnabled() || !IsOverlaySwapAvailable()
                || !TryResolveTarget(ResolveFemaleIndex(femaleIndex), out ChaControl target, out string error))
            {
                LogWarnAlways("tattoo overlay clear failed: target or KoiSkinOverlayX unavailable");
                return false;
            }
            try
            {
                Component controller = target.gameObject.GetComponent(_ksoxControllerType);
                object storage = _ksoxControllerType.GetProperty("OverlayStorage", BindingFlags.Public | BindingFlags.Instance)?.GetValue(controller, null);
                if (storage == null) return false;
                MethodInfo set = storage.GetType().GetMethod("SetTexture", BindingFlags.Public | BindingFlags.Instance,
                    null, new[] { _ksoxTexTypeEnum, typeof(byte[]) }, null);
                MethodInfo update = _ksoxControllerType.GetMethod("UpdateTexture", BindingFlags.Public | BindingFlags.Instance,
                    null, new[] { _ksoxTexTypeEnum }, null);
                if (set == null || update == null) return false;
                if (face) set.Invoke(storage, new object[] { Enum.Parse(_ksoxTexTypeEnum, "FaceOver"), null });
                if (body) set.Invoke(storage, new object[] { Enum.Parse(_ksoxTexTypeEnum, "BodyOver"), null });
                if (face) update.Invoke(controller, new[] { Enum.Parse(_ksoxTexTypeEnum, "FaceOver") });
                if (body) update.Invoke(controller, new[] { Enum.Parse(_ksoxTexTypeEnum, "BodyOver") });
                return true;
            }
            catch (Exception ex) { LogWarnAlways("tattoo overlay clear failed: " + ex); return false; }
        }

        internal bool TryGetTattooOverlays(out byte[] face, out byte[] body, int femaleIndex)
        {
            face = null; body = null;
            if (!IsEnabled() || !IsOverlaySwapAvailable()
                || !TryResolveTarget(ResolveFemaleIndex(femaleIndex), out ChaControl target, out string error)) return false;
            try
            {
                Component controller = target.gameObject.GetComponent(_ksoxControllerType);
                object storage = _ksoxControllerType.GetProperty("OverlayStorage", BindingFlags.Public | BindingFlags.Instance)?.GetValue(controller, null);
                if (storage == null) return false;
                MethodInfo get = storage.GetType().GetMethod("GetTexture", BindingFlags.Public | BindingFlags.Instance,
                    null, new[] { _ksoxTexTypeEnum }, null);
                if (get == null) return false;
                Texture2D faceTexture = get.Invoke(storage, new[] { Enum.Parse(_ksoxTexTypeEnum, "FaceOver") }) as Texture2D;
                Texture2D bodyTexture = get.Invoke(storage, new[] { Enum.Parse(_ksoxTexTypeEnum, "BodyOver") }) as Texture2D;
                face = faceTexture?.EncodeToPNG();
                body = bodyTexture?.EncodeToPNG();
                return true;
            }
            catch (Exception ex) { LogWarnAlways("tattoo overlay read failed: " + ex); return false; }
        }

        private static byte[] ReadTattooPng(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            byte[] bytes = File.ReadAllBytes(path.Trim().Trim('"'));
            if (bytes.Length < 8 || bytes[0] != 137 || bytes[1] != 80 || bytes[2] != 78 || bytes[3] != 71
                || bytes[4] != 13 || bytes[5] != 10 || bytes[6] != 26 || bytes[7] != 10)
                throw new InvalidDataException("Not a PNG: " + path);
            return bytes;
        }

        private static bool IsOverlaySwapAvailable()
        {
            ResolveOverlayApi();
            return _extGetById != null && _ksoxControllerType != null && _ksoxTexTypeEnum != null;
        }

        /// <summary>カード(chaFile)からKSOXの上絵データを取り出す。無ければnull。</summary>
        private object GetOverlayDataFromFile(ChaFile file)
        {
            if (file == null || !IsOverlaySwapAvailable())
                return null;

            try
            {
                return _extGetById.Invoke(null, new object[] { file, KsoxExtDataId });
            }
            catch (Exception ex)
            {
                LogWarnAlways("overlay data read failed: " + ex.Message);
                return null;
            }
        }

        internal void CaptureOriginalOverlay(ChaControl target)
        {
            _originalOverlayData = target != null ? GetOverlayDataFromFile(target.chaFile) : null;
        }

        internal void CaptureTargetOverlay(ChaFileControl card)
        {
            _targetOverlayData = GetOverlayDataFromFile(card);
        }

        internal void ResetOverlaySwapState()
        {
            _overlaySwapped = false;
        }

        /// <summary>
        /// 遷移の中間点で1回だけ、行き先の上絵一式へ差し替える。
        /// 行き先にKSOXデータが無い場合は上絵を消す（元が上絵持ちなら消さないと残る）。
        /// </summary>
        internal void TickOverlaySwap(ChaControl target, float targetBlend, float timeRatio)
        {
            if (_overlaySwapped || timeRatio < 0.5f)
                return;

            if (_settings == null || !_settings.MorphColorEnabled || !_settings.MorphColorBody)
                return;

            // 顔を覆う上絵を持つカードでは、差し替えた上絵が肌色の変化を隠してしまう。
            // どちらを取るかはカード次第なので設定で切り替える。
            if (!_settings.MorphOverlaySwapEnabled)
                return;

            _overlaySwapped = true;

            if (!IsOverlaySwapAvailable() || target == null)
                return;

            object data = targetBlend >= 0.5f ? _targetOverlayData : _originalOverlayData;
            ApplyOverlayData(target, data);
        }

        private void ApplyOverlayData(ChaControl target, object pluginData)
        {
            try
            {
                Component controller = target.gameObject.GetComponent(_ksoxControllerType);
                if (controller == null)
                {
                    LogExecution("overlay swap skipped: controller not on character");
                    return;
                }

                PropertyInfo storageProp = _ksoxControllerType.GetProperty(
                    "OverlayStorage", BindingFlags.Public | BindingFlags.Instance);
                object storage = storageProp != null ? storageProp.GetValue(controller, null) : null;
                if (storage == null)
                {
                    LogWarnAlways("overlay swap failed: storage not found");
                    return;
                }

                if (pluginData != null)
                {
                    MethodInfo load = storage.GetType().GetMethod(
                        "Load", BindingFlags.Public | BindingFlags.Instance, null,
                        new[] { pluginData.GetType() }, null);
                    if (load == null)
                    {
                        LogWarnAlways("overlay swap failed: Load(PluginData) not found");
                        return;
                    }
                    load.Invoke(storage, new[] { pluginData });
                }
                else
                {
                    MethodInfo clear = storage.GetType().GetMethod(
                        "Clear", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    clear?.Invoke(storage, new object[0]);
                }

                // 差し替え後に何を持っているかを記録（顔上絵の有無の証拠になる）
                LogOverlayTypes(storage);

                MethodInfo update = _ksoxControllerType.GetMethod(
                    "UpdateTexture", BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { _ksoxTexTypeEnum }, null);
                if (update != null)
                {
                    object unknown = Enum.ToObject(_ksoxTexTypeEnum, 0); // TexType.Unknown
                    update.Invoke(controller, new[] { unknown });
                }

                // KSOXの再構築はゲーム標準のベーステクスチャから合成し直す。
                // その直後にこちらの肌色を塗り直さないと、色が上絵ごと元に戻る。
                _forceColorAfterOverlay = true;
                LogExecution("overlay swap applied: data=" + (pluginData != null ? "card" : "clear"));
            }
            catch (Exception ex)
            {
                LogWarnAlways("overlay swap failed: " + ex.Message);
            }
        }

        private void LogOverlayTypes(object storage)
        {
            try
            {
                MethodInfo getAll = storage.GetType().GetMethod(
                    "GetAllTypes", BindingFlags.NonPublic | BindingFlags.Instance);
                var types = getAll != null ? getAll.Invoke(storage, new object[0]) as System.Collections.IEnumerable : null;
                if (types == null)
                    return;

                var names = new System.Collections.Generic.List<string>();
                foreach (object t in types)
                    names.Add(t != null ? t.ToString() : "null");
                LogExecution("overlay types now: " + (names.Count > 0 ? string.Join(", ", names.ToArray()) : "(none)"));
            }
            catch
            {
            }
        }
    }
}
