using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MainGameCharacterMorphBridge
{
    /// <summary>
    /// 色の補間。体型と同じく、元キャラの色とカードの色を混ぜて反映する。
    ///
    /// 色はゲームのバージョンで持ち方が変わりうるので、メンバも反映関数も名前で引く。
    /// 見つからないものは黙って飛ばし、何が使えたかは起動後の初回に一度だけログへ出す。
    /// こうしておけば、名前が違っていても他の色は動く。
    ///
    /// 反映はテクスチャの作り直しを伴う場合があるため、遷移中は間引いて呼ぶ。
    /// </summary>
    public sealed partial class Plugin
    {
        private sealed class ColorMember
        {
            internal FieldInfo Field;
            internal PropertyInfo Property;
        }

        private static readonly Dictionary<Type, Dictionary<string, ColorMember>> ColorMemberCache =
            new Dictionary<Type, Dictionary<string, ColorMember>>();

        private static ColorMember FindColorMember(Type type, string name)
        {
            lock (ColorMemberCache)
            {
                if (!ColorMemberCache.TryGetValue(type, out var members))
                    ColorMemberCache[type] = members = new Dictionary<string, ColorMember>();
                if (members.TryGetValue(name, out var cached))
                    return cached;

                var result = new ColorMember();
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                for (Type current = type; current != null; current = current.BaseType)
                {
                    if (result.Field == null)
                        result.Field = current.GetField(name, flags);
                    if (result.Property == null)
                        result.Property = current.GetProperty(name, flags);
                    if (result.Field != null && result.Property != null)
                        break;
                }
                members[name] = result;
                return result;
            }
        }

        internal bool TryGetSkin(out Color color, out int skinId, int femaleIndex)
        {
            return TryGetSkin(out color, out _, out skinId, femaleIndex);
        }

        internal bool TryGetSkin(out Color main, out Color sub, out int skinId, int femaleIndex)
        {
            main = Color.white;
            sub = Color.white;
            skinId = 0;
            if (!TryResolveTarget(ResolveFemaleIndex(femaleIndex), out ChaControl target, out _)) return false;
            object body = target.chaFile?.custom?.body;
            if (body == null || !TryGetColor(body, "skinMainColor", out main)) return false;
            if (!TryGetColor(body, "skinSubColor", out sub)) sub = main;
            TryGetInt(body, "skinId", out skinId);
            return true;
        }

        internal bool SetSkin(Color color, int skinId, int femaleIndex)
        {
            return SetSkin(color, color, skinId, femaleIndex);
        }

        internal bool SetSkin(Color main, Color sub, int skinId, int femaleIndex)
        {
            if (!IsEnabled() || !TryResolveTarget(ResolveFemaleIndex(femaleIndex), out ChaControl target, out _)) return false;
            object body = target.chaFile?.custom?.body;
            if (body == null) return false;
            PrepareCustomTextureInputs(target);
            bool changed = TrySetColor(body, "skinMainColor", main);
            changed |= TrySetColor(body, "skinSubColor", sub);
            if (skinId >= 0)
            {
                changed |= TrySetInt(body, "skinId", skinId);
                TrySetInt(target, "skinId", skinId);
            }
            TrySetColor(target, "skinMainColor", main);
            TrySetColor(target, "skinSubColor", sub);
            InvokeRefreshMethod(target, "AddUpdateCMBodyColorFlags", -1);
            InvokeRefreshMethod(target, "CreateBodyTexture", -1);
            InvokeRefreshMethod(target, "AddUpdateCMFaceColorFlags", -1);
            InvokeRefreshMethod(target, "CreateFaceTexture", -1);
            return changed;
        }

        // ChaFileHair.parts[i] が持つ色。
        private static readonly string[] HairColorMembers =
            { "baseColor", "startColor", "endColor", "glossColor", "outlineColor", "acsColor" };

        // ChaFileFace が持つ色。
        // eyeshadowColor / lipColor はここには無く ChaFileMakeup 側にある(逆コンパイルで確認済み)。
        // 以前は入れていたが、書き込み先が無いので黙って捨てられていた。化粧は Plugin.MorphMakeup.cs で扱う。
        private static readonly string[] FaceColorMembers =
            { "eyebrowColor", "eyelineColor", "hlUpColor", "hlDownColor" };

        // ChaFileBody が持つ色。
        private static readonly string[] BodyColorMembers =
            { "skinMainColor", "skinSubColor", "sunburnColor", "nipColor", "underhairColor" };

        // 反映関数。パーツ番号を取るものと取らないものが混在するので、引数の数で呼び分ける。
        private static readonly string[] HairRefreshMethods =
            { "ChangeSettingHairColor", "ChangeSettingHairGlossColor", "ChangeSettingHairOutlineColor", "ChangeSettingHairAcsColor" };

        private static readonly string[] FaceRefreshMethods =
            { "ChangeSettingEyebrowColor", "ChangeSettingEyelineColor", "ChangeSettingEyeShadowColor", "ChangeSettingLipColor" };

        private static readonly string[] BodyRefreshMethods =
            { "ChangeSettingNipColor", "ChangeSettingUnderhairColor", "ChangeSettingSkinGlossPower" };

        private float _nextColorApplyTime;
        private bool _colorSupportLogged;
        private bool _hairColorDiffLogged;
        private bool _skinTargetsLogged;
        private bool _colorApplyLogged;
        private bool ColorDiagnosticsEnabled => _runtimeLoggingEnabled && _settings != null && _settings.EnableLogs;

        /// <summary>色の書き込みがどこまで通ったかを1回だけ残す。</summary>
        private void LogColorApplyOnce(bool hair, bool face, bool body)
        {
            if (!ColorDiagnosticsEnabled || _colorApplyLogged)
                return;
            _colorApplyLogged = true;
            LogExecution("color applied: hair=" + hair + " face=" + face + " body=" + body);
        }

        /// <summary>
        /// 肌の色は専用の反映関数が無く、プロパティ代入で反映される前提で書いている。
        /// その書き込み先が本当に存在するのかを、ChaFileBody側とChaControl側の両方で確かめる。
        /// </summary>
        /// <summary>
        /// 元キャラとカードの色を並べる。遷移の起点ではなく必ず元キャラ側と比べる。
        /// 起点と比べると、同じカードへの変身では差が出ずに判断を誤る。
        /// カードを読むたびに出す。
        /// </summary>
        internal void LogOriginalVsCardColors(ChaControl target)
        {
            if (!ColorDiagnosticsEnabled) return;
            ColorSnapshot fromColors = _originalColors;
            ColorSnapshot toColors = _targetColors;
            if (target == null || target.chaFile == null || fromColors == null || toColors == null)
                return;

            object body = target.chaFile.custom != null ? target.chaFile.custom.body : null;
            for (int i = 0; i < BodyColorMembers.Length; i++)
            {
                string member = BodyColorMembers[i];
                bool onFile = HasWritableMember(body, member, typeof(Color));
                bool onControl = HasWritableMember(target, member, typeof(Color));

                string values = string.Empty;
                if (fromColors != null && toColors != null
                    && fromColors.Body != null && toColors.Body != null
                    && i < fromColors.Body.Length && i < toColors.Body.Length)
                {
                    Color f = fromColors.Body[i];
                    Color t = toColors.Body[i];
                    values = " from=" + f.r.ToString("0.00") + "," + f.g.ToString("0.00") + "," + f.b.ToString("0.00")
                           + " to=" + t.r.ToString("0.00") + "," + t.g.ToString("0.00") + "," + t.b.ToString("0.00")
                           + (f == t ? " (同じ色)" : string.Empty);
                }

                LogExecution("card vs original " + member + ": chaFile=" + onFile + " chaControl=" + onControl + values);
            }

            LogHairColorDiff(Mathf.Min(fromColors.HairParts, toColors.HairParts));

            if (fromColors != null && toColors != null)
            {
                LogExecution("card vs original kind: skinId " + (fromColors.HasSkinId ? fromColors.SkinId.ToString() : "?")
                    + " -> " + (toColors.HasSkinId ? toColors.SkinId.ToString() : "?")
                    + " / sunburnKind " + (fromColors.HasSunburnKind ? fromColors.SunburnKind.ToString() : "?")
                    + " -> " + (toColors.HasSunburnKind ? toColors.SunburnKind.ToString() : "?"));
            }
        }

        private bool _chaControlMethodsLogged;
        private bool _updateFlagLogged;
        private bool _matLinkLogged;
        private bool _customTexInputsPrepared;

        /// <summary>
        /// 本編では ChaInfo.releaseCustomInputTexture が true なので、キャラ生成が終わった直後に
        /// ReleaseBodyCustomTexture / ReleaseFaceCustomTexture が走り、肌テクスチャの合成に使う
        /// 入力テクスチャ(ベース・カラーマスク・ペイント・日焼け・リップライン・ホクロ)が
        /// 合成用マテリアルから外される。逆コンパイルで確認済み(CreateBodyTexture 末尾/CreateFaceTexture 末尾)。
        ///
        /// その状態で色フラグだけ立てて CreateFaceTexture を呼び直すと、
        /// 入力が空のまま焼き直すので出来上がりがほぼ真っ白になり、肌色を混ぜても見た目が動かない。
        /// 焼き直す前に一度だけ Tex/Layout フラグまで立てて入力を読み込み直し、
        /// 以後は外されないように解放フラグを下ろしておく。
        /// </summary>
        private void PrepareCustomTextureInputs(ChaControl target)
        {
            if (target == null)
                return;

            // 一度やったかどうかを自前で覚えても当てにならない。キャラが読み込み直されると
            // ChaInfo.MemberInitializeAll() が releaseCustomInputTexture を true へ戻すため、
            // Hシーンを出入りしたりキャラクリへ行って戻ると、こちらの記憶だけが取り残される。
            // その状態で焼き直すと入力が外れたまま焼くので、顔から化粧やペイントが消える。
            // 覚えるのをやめて、毎回ゲーム側の実物を見る。読むだけなので安い。
            object flag = GetMemberValue(target, "releaseCustomInputTexture");
            bool needsPrepare = !(flag is bool) || (bool)flag;
            if (_customTexInputsPrepared && !needsPrepare)
                return;

            _customTexInputsPrepared = true;

            // 先に下ろさないと、この読み込み直しの直後にまた外される。
            bool released = TrySetBool(target, "releaseCustomInputTexture", false);

            InvokeRefreshMethod(target, "AddUpdateCMBodyTexFlags", -1);
            InvokeRefreshMethod(target, "AddUpdateCMBodyLayoutFlags", -1);
            InvokeRefreshMethod(target, "AddUpdateCMBodyColorFlags", -1);
            bool body = InvokeRefreshMethod(target, "CreateBodyTexture", -1);

            InvokeRefreshMethod(target, "AddUpdateCMFaceTexFlags", -1);
            InvokeRefreshMethod(target, "AddUpdateCMFaceLayoutFlags", -1);
            InvokeRefreshMethod(target, "AddUpdateCMFaceColorFlags", -1);
            bool face = InvokeRefreshMethod(target, "CreateFaceTexture", -1);

            LogExecution("custom texture inputs restored: releaseFlagCleared=" + released
                + " body=" + body + " face=" + face);
        }

        /// <summary>解放フラグをゲーム既定へ戻す。Hシーンを抜ける時に呼ぶ。</summary>
        internal void ReleaseCustomTextureInputs(ChaControl target)
        {
            if (!_customTexInputsPrepared)
                return;

            _customTexInputsPrepared = false;
            if (target == null)
                return;

            TrySetBool(target, "releaseCustomInputTexture", true);
            InvokeRefreshMethod(target, "ReleaseBodyCustomTexture", -1);
            InvokeRefreshMethod(target, "ReleaseFaceCustomTexture", -1);
        }

        private bool _overlayLogged;
        private bool _texDumped;

        /// <summary>
        /// 再構築後の体・顔テクスチャを実際に読み出して平均色を測り、PNGにも残す。
        /// 「テクスチャ自体は正しく暗くなっているのに表示だけ変わらない」のか
        /// 「テクスチャがそもそも塗れていない」のかを、ここで最終的に分ける。
        /// </summary>
        private void DumpCreatedTexturesOnce(ChaControl target)
        {
            if (!ColorDiagnosticsEnabled || _texDumped || target == null)
                return;
            _texDumped = true;

            DumpCreatedTexture(target, "customTexCtrlBody", "body");
            DumpCreatedTexture(target, "customTexCtrlFace", "face");
        }

        private void DumpCreatedTexture(ChaControl target, string ctrlName, string label)
        {
            try
            {
                object ctrl = GetMemberValue(target, ctrlName);
                var rt = ctrl != null ? GetMemberValue(ctrl, "createTex") as RenderTexture : null;
                if (rt == null)
                {
                    LogExecution("tex dump " + label + ": createTex null");
                    return;
                }

                RenderTexture prev = RenderTexture.active;
                Texture2D tex = null;
                try
                {
                    RenderTexture.active = rt;
                    tex = new Texture2D(rt.width, rt.height, TextureFormat.ARGB32, false);
                    tex.ReadPixels(new Rect(0f, 0f, rt.width, rt.height), 0, 0);

                    Color32[] pixels = tex.GetPixels32();
                    double r = 0, g = 0, b = 0;
                    int count = 0;
                    for (int i = 0; i < pixels.Length; i += 16)
                    {
                        r += pixels[i].r; g += pixels[i].g; b += pixels[i].b; count++;
                    }
                    if (count <= 0) count = 1;

                    string path = System.IO.Path.Combine(_pluginDir, "texdump_" + label + ".png");
                    System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());

                    LogExecution("tex dump " + label + ": " + rt.width + "x" + rt.height
                        + " mean=" + (r / count / 255.0).ToString("0.00")
                        + "," + (g / count / 255.0).ToString("0.00")
                        + "," + (b / count / 255.0).ToString("0.00")
                        + " file=" + path);
                }
                finally
                {
                    RenderTexture.active = prev;
                    if (tex != null)
                        Destroy(tex);
                }
            }
            catch (Exception ex)
            {
                LogWarnAlways("tex dump " + label + " failed: " + ex.Message);
            }
        }

        /// <summary>
        /// キャラに付いているオーバーレイ系コンポーネントと、その保持テクスチャを列挙する。
        /// 顔に不透明な上絵が乗っていれば、下の肌色をいくら塗り直しても見た目が変わらない。
        /// その確定用。
        /// </summary>
        private void LogOverlayStateOnce(ChaControl target)
        {
            if (!ColorDiagnosticsEnabled || _overlayLogged || target == null)
                return;
            _overlayLogged = true;

            try
            {
                foreach (MonoBehaviour component in target.gameObject.GetComponents<MonoBehaviour>())
                {
                    if (component == null)
                        continue;

                    string typeName = component.GetType().FullName ?? string.Empty;
                    if (typeName.IndexOf("Overlay", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    LogExecution("overlay component: " + typeName);

                    // KoiSkinOverlayController の中身を覗く。フィールド名は版によって違うので全列挙。
                    foreach (FieldInfo field in component.GetType().GetFields(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        object value;
                        try { value = field.GetValue(component); }
                        catch { continue; }
                        if (value == null)
                            continue;

                        var dict = value as System.Collections.IDictionary;
                        if (dict != null && dict.Count > 0)
                        {
                            var keys = new List<string>();
                            foreach (object key in dict.Keys)
                                keys.Add(key != null ? key.ToString() : "null");
                            LogExecution("overlay data " + field.Name + " (" + dict.Count + "): " + string.Join(", ", keys.ToArray()));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogWarnAlways("overlay state log failed: " + ex.Message);
            }
        }

        /// <summary>
        /// 塗り直し先(matDraw)とレンダラの実マテリアルがつながっているかを体・顔で比べる。
        /// MaterialEditor等がマテリアルを差し替えていると、塗り直しても画面に届かない。
        /// 体は効いて顔だけ効かない場合、その差がここに出る。
        /// </summary>
        private void LogFaceMaterialLinkOnce(ChaControl target)
        {
            if (!ColorDiagnosticsEnabled || _matLinkLogged || target == null)
                return;
            _matLinkLogged = true;

            try
            {
                LogMatLink(target, "body", "rendBody", "customTexCtrlBody");
                LogMatLink(target, "face", "rendFace", "customTexCtrlFace");
            }
            catch (Exception ex)
            {
                LogWarnAlways("mat link log failed: " + ex.Message);
            }
        }

        private void LogMatLink(ChaControl target, string label, string rendName, string texCtrlName)
        {
            var rend = GetMemberValue(target, rendName) as Renderer;
            object texCtrl = GetMemberValue(target, texCtrlName);
            var matDraw = texCtrl != null ? GetMemberValue(texCtrl, "matDraw") as Material : null;
            Material current = rend != null ? rend.sharedMaterial : null;

            LogExecution("mat link " + label
                + ": rend=" + (rend != null)
                + " matDraw=" + (matDraw != null ? matDraw.name : "null")
                + " current=" + (current != null ? current.name : "null")
                + " shader=" + (current != null && current.shader != null ? current.shader.name : "null")
                + " linked=" + (matDraw != null && ReferenceEquals(current, matDraw)));
        }

        /// <summary>
        /// ChaControl が実際に持っている色・肌・髪まわりの関数を列挙する。
        /// 反映方法を推測で決めないための材料。引数なしか int 1個のものだけ拾う。
        /// </summary>
        private void LogChaControlColorMethodsOnce(ChaControl target)
        {
            if (!ColorDiagnosticsEnabled || _chaControlMethodsLogged || target == null)
                return;
            _chaControlMethodsLogged = true;

            try
            {
                var found = new List<string>();
                foreach (MethodInfo method in target.GetType().GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (method == null || method.IsGenericMethod)
                        continue;

                    string name = method.Name;
                    string lower = name.ToLowerInvariant();
                    if (!(lower.Contains("skin") || lower.Contains("color") || lower.Contains("sunburn")
                        || lower.Contains("bodytexture") || lower.Contains("custombody")
                        || lower.Contains("createbody") || lower.Contains("updatebody")
                        || lower.Contains("texture")))
                        continue;

                    ParameterInfo[] ps = method.GetParameters();
                    if (ps.Length == 0)
                        found.Add(name + "()");
                    else if (ps.Length == 1 && ps[0].ParameterType == typeof(int))
                        found.Add(name + "(int)");
                }

                found.Sort(StringComparer.Ordinal);
                LogExecution("chacontrol color methods: " + string.Join(", ", found.ToArray()));
            }
            catch (Exception ex)
            {
                LogWarnAlways("chacontrol method scan failed: " + ex.Message);
            }
        }

        private static bool HasWritableMember(object owner, string name, Type expected)
        {
            if (owner == null)
                return false;

            ColorMember member = FindColorMember(owner.GetType(), name);
            FieldInfo field = member.Field;
            if (field != null && field.FieldType == expected)
                return true;

            PropertyInfo property = member.Property;
            return property != null && property.CanWrite && property.PropertyType == expected;
        }

        /// <summary>
        /// 元とカードの髪色を並べて1回だけ出す。色が動かない時に
        /// 「書けていない」のか「そもそも同じ色」なのかを分けるための計測。
        /// </summary>
        private void LogHairColorDiff(int partCount)
        {
            for (int p = 0; p < partCount; p++)
            {
                int index = p * HairColorMembers.Length; // baseColor
                if (index >= _originalColors.Hair.Length || index >= _targetColors.Hair.Length)
                    continue;

                Color from = _originalColors.Hair[index];
                Color to = _targetColors.Hair[index];
                LogExecution("hair base color part=" + p
                    + " from=" + from.r.ToString("0.00") + "," + from.g.ToString("0.00") + "," + from.b.ToString("0.00")
                    + " to=" + to.r.ToString("0.00") + "," + to.g.ToString("0.00") + "," + to.b.ToString("0.00")
                    + (from == to ? " (同じ色)" : ""));
            }
        }

        // ---------------- スナップショット ----------------

        /// <summary>色一式。体型スナップショットと同じ形で持つ。</summary>
        internal sealed class ColorSnapshot
        {
            internal Color[] Hair;   // parts数 × HairColorMembers数
            internal int HairParts;
            internal Color[] Face;
            internal Color[] Body;
            internal float SkinGlossPower;
            internal bool HasSkinGlossPower;
            // 肌の濃さは色ではなく種類(テクスチャ)で決まる。IDなので中間値は無い。
            internal int SkinId;
            internal bool HasSkinId;
            internal int SunburnKind;
            internal bool HasSunburnKind;

            internal bool HasAny => (Hair != null && Hair.Length > 0)
                || (Face != null && Face.Length > 0)
                || (Body != null && Body.Length > 0);
        }

        internal static ColorSnapshot ColorSnapshotFrom(ChaFileControl file)
        {
            if (file == null || file.custom == null)
                return null;

            var snapshot = new ColorSnapshot();

            object[] hairParts = GetHairParts(file);
            if (hairParts != null && hairParts.Length > 0)
            {
                snapshot.HairParts = hairParts.Length;
                snapshot.Hair = new Color[hairParts.Length * HairColorMembers.Length];
                for (int p = 0; p < hairParts.Length; p++)
                {
                    for (int m = 0; m < HairColorMembers.Length; m++)
                    {
                        Color value;
                        snapshot.Hair[p * HairColorMembers.Length + m] =
                            TryGetColor(hairParts[p], HairColorMembers[m], out value) ? value : Color.clear;
                    }
                }
            }

            snapshot.Face = ReadColors(file.custom.face, FaceColorMembers);
            snapshot.Body = ReadColors(file.custom.body, BodyColorMembers);

            float gloss;
            snapshot.HasSkinGlossPower = TryGetFloat(file.custom.body, "skinGlossPower", out gloss);
            snapshot.SkinGlossPower = gloss;

            int skinId;
            snapshot.HasSkinId = TryGetInt(file.custom.body, "skinId", out skinId);
            snapshot.SkinId = skinId;

            int sunburnKind;
            snapshot.HasSunburnKind = TryGetInt(file.custom.body, "sunburnKind", out sunburnKind);
            snapshot.SunburnKind = sunburnKind;

            return snapshot;
        }

        private static Color[] ReadColors(object container, string[] members)
        {
            if (container == null)
                return null;

            var colors = new Color[members.Length];
            for (int i = 0; i < members.Length; i++)
            {
                Color value;
                colors[i] = TryGetColor(container, members[i], out value) ? value : Color.clear;
            }
            return colors;
        }

        /// <summary>
        /// 髪パーツの配列そのものを返す。要素が構造体だった場合、GetValue で取り出したものは
        /// コピーなので、書き換えたら SetValue で配列へ戻さないと反映されない。
        /// </summary>
        private static Array GetHairPartsArray(ChaFileControl file)
        {
            if (file == null || file.custom == null)
                return null;

            object hair = GetMemberValue(file.custom, "hair");
            if (hair == null)
                return null;

            var parts = GetMemberValue(hair, "parts") as Array;
            return (parts != null && parts.Length > 0) ? parts : null;
        }

        private static object[] GetHairParts(ChaFileControl file)
        {
            Array parts = GetHairPartsArray(file);
            if (parts == null)
                return null;

            var result = new object[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                result[i] = parts.GetValue(i);
            return result;
        }

        // ---------------- 反映 ----------------

        /// <summary>
        /// 色を混ぜて反映する。遷移中は間引く。<paramref name="force"/> は遷移の最後など、
        /// 必ず最終値を入れたい時に使う。
        /// </summary>
        private bool ApplyColorBlend(ChaControl target, float blend, bool force)
        {
            return ApplyColorBlendBetween(target, _originalColors, _targetColors, _originalMakeup, _targetMakeup, blend, force);
        }

        /// <summary>起点と行き先を明示して混ぜる。遷移中は開始時点の色を起点にする。</summary>
        private bool ApplyColorBlendBetween(
            ChaControl target, ColorSnapshot fromColors, ColorSnapshot toColors, float blend, bool force)
        {
            return ApplyColorBlendBetween(target, fromColors, toColors, null, null, blend, force);
        }

        private bool ApplyColorBlendBetween(
            ChaControl target, ColorSnapshot fromColors, ColorSnapshot toColors,
            MakeupSnapshot fromMakeup, MakeupSnapshot toMakeup, float blend, bool force)
        {
            if (_settings == null || !_settings.MorphColorEnabled)
                return false;

            if (target == null || target.chaFile == null || target.chaFile.custom == null)
                return false;

            if (fromColors == null || toColors == null)
                return false;

            if (!force)
            {
                float interval = Mathf.Max(0f, _settings.MorphColorIntervalSeconds);
                if (Time.unscaledTime < _nextColorApplyTime)
                    return false;
                _nextColorApplyTime = Time.unscaledTime + interval;
            }

            // 焼き直しの入力を先に戻す。これが無いと顔・体のテクスチャが白く潰れる。
            PrepareCustomTextureInputs(target);

            blend = Mathf.Clamp01(blend);
            bool hairChanged = false;
            bool faceChanged = false;
            bool bodyChanged = false;

            if (_settings.MorphColorHair)
                hairChanged = ApplyHairColors(target, fromColors, toColors, blend, force);

            if (_settings.MorphColorFace)
                faceChanged = ApplyContainerColors(
                    target.chaFile.custom.face, FaceColorMembers, fromColors.Face, toColors.Face, blend, force);

            if (_settings.MorphColorBody)
            {
                bodyChanged = ApplyContainerColors(
                    target.chaFile.custom.body, BodyColorMembers, fromColors.Body, toColors.Body, blend, force);

                if (fromColors.HasSkinGlossPower && toColors.HasSkinGlossPower)
                {
                    float gloss = Mathf.Lerp(fromColors.SkinGlossPower, toColors.SkinGlossPower, blend);
                    if (force || !TryGetFloat(target.chaFile.custom.body, "skinGlossPower", out float currentGloss)
                        || !currentGloss.Equals(gloss))
                        bodyChanged |= TrySetFloat(target.chaFile.custom.body, "skinGlossPower", gloss);
                    // 肌の色と光沢は ChaControl 側のプロパティに入れると反映処理が走る。
                }

            }

            // 化粧とペイント。色は混ぜ、テクスチャ番号は時間の真ん中で切り替える。
            bool makeupFaceColor = false, makeupFaceTex = false, makeupFaceLayout = false;
            bool makeupBodyColor = false, makeupBodyTex = false, makeupBodyLayout = false;
            if (_settings.MorphColorMakeup)
                ApplyMakeupBlend(target, fromMakeup, toMakeup, blend, force,
                    out makeupFaceColor, out makeupFaceTex, out makeupFaceLayout,
                    out makeupBodyColor, out makeupBodyTex, out makeupBodyLayout);

            faceChanged |= makeupFaceColor || makeupFaceTex || makeupFaceLayout;
            bodyChanged |= makeupBodyColor || makeupBodyTex || makeupBodyLayout;

            // 番号やレイアウトを変えた時は、色フラグだけでは焼き直されない。
            if (makeupFaceTex)
                InvokeRefreshMethod(target, "AddUpdateCMFaceTexFlags", -1);
            if (makeupFaceLayout)
                InvokeRefreshMethod(target, "AddUpdateCMFaceLayoutFlags", -1);
            if (makeupBodyTex)
                InvokeRefreshMethod(target, "AddUpdateCMBodyTexFlags", -1);
            if (makeupBodyLayout)
                InvokeRefreshMethod(target, "AddUpdateCMBodyLayoutFlags", -1);

            if (hairChanged)
                InvokeRefreshPerPart(target, HairRefreshMethods, fromColors.HairParts);
            if (faceChanged)
                InvokeRefresh(target, FaceRefreshMethods);
            if (bodyChanged)
            {
                InvokeRefresh(target, BodyRefreshMethods);
                // ゲーム側の実装(逆コンパイルで確認済み):
                //   AddUpdateCMBodyColorFlags(全true) でフラグ配列を立ててから CreateBodyTexture() を呼ぶ。
                // CreateBodyTexture はフラグが立っている項目しか塗り直さないので、順序が必須。
                bool flagged = InvokeRefreshMethod(target, "AddUpdateCMBodyColorFlags", -1);
                bool rebuilt = InvokeRefreshMethod(target, "CreateBodyTexture", -1);
                // 顔の肌も fileBody.skinMainColor から塗られる(CreateFaceTexture 3523行目)。
                // 体の肌色が変わったら顔テクスチャも作り直さないと、顔だけ元の色で残る。
                InvokeRefreshMethod(target, "AddUpdateCMFaceColorFlags", -1);
                InvokeRefreshMethod(target, "CreateFaceTexture", -1);
                if (!_updateFlagLogged)
                {
                    _updateFlagLogged = true;
                    LogExecution("skin refresh: flags=" + flagged + " CreateBodyTexture=" + rebuilt);
                }

                LogFaceMaterialLinkOnce(target);
                LogOverlayStateOnce(target);
                DumpCreatedTexturesOnce(target);
            }

            // 体色更新時は上の分岐で顔テクスチャも再生成済み。
            if (faceChanged && !bodyChanged)
            {
                InvokeRefreshMethod(target, "AddUpdateCMFaceColorFlags", -1);
                InvokeRefreshMethod(target, "CreateFaceTexture", -1);
            }

            LogColorSupportOnce(target);
            LogChaControlColorMethodsOnce(target);
            LogColorApplyOnce(hairChanged, faceChanged, bodyChanged);
            return hairChanged || faceChanged || bodyChanged;
        }

        private bool ApplyHairColors(ChaControl target, ColorSnapshot fromColors, ColorSnapshot toColors, float blend, bool force)
        {
            if (fromColors.Hair == null || toColors.Hair == null)
                return false;

            Array parts = GetHairPartsArray(target.chaFile);
            if (parts == null)
                return false;

            int partCount = Mathf.Min(parts.Length, Mathf.Min(fromColors.HairParts, toColors.HairParts));
            bool changed = false;

            for (int p = 0; p < partCount; p++)
            {
                object part = parts.GetValue(p);
                if (part == null)
                    continue;

                bool partChanged = false;
                for (int m = 0; m < HairColorMembers.Length; m++)
                {
                    int index = p * HairColorMembers.Length + m;
                    if (index >= fromColors.Hair.Length || index >= toColors.Hair.Length)
                        continue;

                    Color from = fromColors.Hair[index];
                    Color to = toColors.Hair[index];
                    if (from == Color.clear && to == Color.clear)
                        continue;
                    if (!force && from.Equals(to)
                        && TryGetColor(part, HairColorMembers[m], out Color current)
                        && current.Equals(from))
                        continue;

                    partChanged |= TrySetColor(part, HairColorMembers[m], Color.Lerp(from, to, blend));
                }

                if (partChanged)
                {
                    // 要素が構造体なら GetValue はコピーなので、書き戻さないと反映されない。
                    parts.SetValue(part, p);
                    changed = true;
                }
            }

            return changed;
        }

        private static bool ApplyContainerColors(
            object container, string[] members, Color[] from, Color[] to, float blend, bool force)
        {
            if (container == null || from == null || to == null)
                return false;

            bool changed = false;
            int count = Mathf.Min(members.Length, Mathf.Min(from.Length, to.Length));
            for (int i = 0; i < count; i++)
            {
                if (from[i] == Color.clear && to[i] == Color.clear)
                    continue;
                if (!force && from[i].Equals(to[i])
                    && TryGetColor(container, members[i], out Color current)
                    && current.Equals(from[i]))
                    continue;

                changed |= TrySetColor(container, members[i], Color.Lerp(from[i], to[i], blend));
            }
            return changed;
        }

        /// <summary>肌の色は ChaControl のプロパティに入れると、そのまま反映処理まで走る。</summary>
        private void ApplySkinColorProperties(ChaControl target, ColorSnapshot fromColors, ColorSnapshot toColors, float blend)
        {
            if (fromColors.Body == null || toColors.Body == null)
                return;

            for (int i = 0; i < BodyColorMembers.Length; i++)
            {
                string member = BodyColorMembers[i];
                if (member != "skinMainColor" && member != "skinSubColor")
                    continue;

                if (i >= fromColors.Body.Length || i >= toColors.Body.Length)
                    continue;

                Color from = fromColors.Body[i];
                Color to = toColors.Body[i];
                if (from == Color.clear && to == Color.clear)
                    continue;

                TrySetColor(target, member, Color.Lerp(from, to, blend));
            }
        }

        // ---------------- 反映関数の呼び出し ----------------

        private void InvokeRefresh(ChaControl target, string[] methodNames)
        {
            foreach (string name in methodNames)
                InvokeRefreshMethod(target, name, -1);
        }

        private void InvokeRefreshPerPart(ChaControl target, string[] methodNames, int partCount)
        {
            foreach (string name in methodNames)
            {
                bool perPart = false;
                for (int p = 0; p < Mathf.Max(1, partCount); p++)
                {
                    if (!InvokeRefreshMethod(target, name, p, out perPart))
                        break;
                    if (!perPart)
                        break;
                }
            }
        }

        private bool InvokeRefreshMethod(ChaControl target, string name, int partIndex)
        {
            bool ignored;
            return InvokeRefreshMethod(target, name, partIndex, out ignored);
        }

        private bool InvokeRefreshMethod(ChaControl target, string name, int partIndex, out bool tookIndex)
        {
            tookIndex = false;
            if (target == null)
                return false;

            try
            {
                // 同じ名前でも引数の形はまちまち。int はパーツ番号、bool は true、
                // それ以外は既定値を入れて、とにかく呼べる形に組み立てる。
                MethodInfo method = FindRefreshMethod(target.GetType(), name);
                if (method == null)
                    return false;

                ParameterInfo[] parameters = method.GetParameters();
                var args = new object[parameters.Length];
                for (int i = 0; i < parameters.Length; i++)
                {
                    Type t = parameters[i].ParameterType;
                    if (t == typeof(int))
                    {
                        args[i] = Mathf.Max(0, partIndex);
                        tookIndex = true;
                    }
                    else if (t == typeof(bool))
                    {
                        args[i] = true;
                    }
                    else if (parameters[i].HasDefaultValue)
                    {
                        args[i] = parameters[i].DefaultValue;
                    }
                    else if (t.IsValueType)
                    {
                        args[i] = Activator.CreateInstance(t);
                    }
                    else
                    {
                        args[i] = null;
                    }
                }

                method.Invoke(target, args);
                return true;
            }
            catch (Exception ex)
            {
                LogWarnAlways("color refresh failed: " + name + " " + ex.Message);
                return false;
            }
        }

        /// <summary>名前で引く。同名の多重定義がある場合は引数が少ないものを選ぶ。</summary>
        private static MethodInfo FindRefreshMethod(Type type, string name)
        {
            MethodInfo best = null;
            foreach (MethodInfo method in type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (method == null || method.IsGenericMethod)
                    continue;
                if (!string.Equals(method.Name, name, StringComparison.Ordinal))
                    continue;
                if (best == null || method.GetParameters().Length < best.GetParameters().Length)
                    best = method;
            }
            return best;
        }

        private void LogColorSupportOnce(ChaControl target)
        {
            if (!ColorDiagnosticsEnabled || _colorSupportLogged || target == null)
                return;

            _colorSupportLogged = true;

            var available = new List<string>();
            var missing = new List<string>();
            foreach (string name in HairRefreshMethods)
                (FindRefreshMethod(target.GetType(), name) != null ? available : missing).Add(name);
            foreach (string name in FaceRefreshMethods)
                (FindRefreshMethod(target.GetType(), name) != null ? available : missing).Add(name);
            foreach (string name in BodyRefreshMethods)
                (FindRefreshMethod(target.GetType(), name) != null ? available : missing).Add(name);

            LogExecution("color refresh available: " + string.Join(", ", available.ToArray()));
            if (missing.Count > 0)
                LogExecution("color refresh missing: " + string.Join(", ", missing.ToArray()));
        }

        // ---------------- リフレクション補助 ----------------

        private static object GetMemberValue(object owner, string name)
        {
            if (owner == null)
                return null;

            ColorMember member = FindColorMember(owner.GetType(), name);
            FieldInfo field = member.Field;
            if (field != null)
                return field.GetValue(owner);

            PropertyInfo property = member.Property;
            if (property != null && property.CanRead)
                return property.GetValue(owner, null);

            return null;
        }

        private static bool TryGetColor(object owner, string name, out Color value)
        {
            value = Color.clear;
            object raw = GetMemberValue(owner, name);
            if (raw is Color)
            {
                value = (Color)raw;
                return true;
            }
            return false;
        }

        private static bool TryGetFloat(object owner, string name, out float value)
        {
            value = 0f;
            object raw = GetMemberValue(owner, name);
            if (raw is float)
            {
                value = (float)raw;
                return true;
            }
            return false;
        }

        private static bool TrySetColor(object owner, string name, Color value)
        {
            return TrySetMember(owner, name, value, typeof(Color));
        }

        /// <summary>
        /// 肌の種類と日焼けの種類。IDなので混ぜられない。半分を越えた時点で切り替える。
        /// 色だけ補間しても、KKSの色黒はこちらで決まるので見た目が変わらない。
        /// </summary>
        private bool ApplySkinKind(ChaControl target, ColorSnapshot fromColors, ColorSnapshot toColors, float blend)
        {
            // 切り替えはイージング後の値ではなく経過時間の真ん中で行う。
            // 5秒の変身なら2.5秒の時点。加減速の設定を変えても切り替わる瞬間がずれない。
            float ratio = _transitionTimeRatio >= 0f ? _transitionTimeRatio : blend;
            object body = target.chaFile != null && target.chaFile.custom != null ? target.chaFile.custom.body : null;
            if (body == null)
                return false;

            bool changed = false;

            if (fromColors.HasSkinId && toColors.HasSkinId && fromColors.SkinId != toColors.SkinId)
            {
                int wanted = ratio >= 0.5f ? toColors.SkinId : fromColors.SkinId;
                int current;
                if (!TryGetInt(body, "skinId", out current) || current != wanted)
                {
                    changed |= TrySetInt(body, "skinId", wanted);
                    TrySetInt(target, "skinId", wanted);
                    if (changed)
                        LogExecution("skin kind switched: skinId=" + wanted);
                }
            }

            if (fromColors.HasSunburnKind && toColors.HasSunburnKind && fromColors.SunburnKind != toColors.SunburnKind)
            {
                int wanted = ratio >= 0.5f ? toColors.SunburnKind : fromColors.SunburnKind;
                int current;
                if (!TryGetInt(body, "sunburnKind", out current) || current != wanted)
                {
                    changed |= TrySetInt(body, "sunburnKind", wanted);
                    if (changed)
                        LogExecution("sunburn kind switched: sunburnKind=" + wanted);
                }
            }

            return changed;
        }

        private static bool TryGetInt(object owner, string name, out int value)
        {
            value = 0;
            object raw = GetMemberValue(owner, name);
            if (raw is int) { value = (int)raw; return true; }
            if (raw is byte) { value = (byte)raw; return true; }
            return false;
        }

        private static bool TrySetInt(object owner, string name, int value)
        {
            return TrySetMember(owner, name, value, typeof(int))
                || TrySetMember(owner, name, (byte)Mathf.Clamp(value, 0, 255), typeof(byte));
        }

        private static bool TrySetBool(object owner, string name, bool value)
        {
            return TrySetMember(owner, name, value, typeof(bool));
        }

        private static bool TrySetFloat(object owner, string name, float value)
        {
            return TrySetMember(owner, name, value, typeof(float));
        }

        private static bool TrySetMember(object owner, string name, object value, Type expected)
        {
            if (owner == null)
                return false;

            try
            {
                ColorMember member = FindColorMember(owner.GetType(), name);
                FieldInfo field = member.Field;
                if (field != null && field.FieldType == expected)
                {
                    field.SetValue(owner, value);
                    return true;
                }

                PropertyInfo property = member.Property;
                if (property != null && property.CanWrite && property.PropertyType == expected)
                {
                    property.SetValue(owner, value, null);
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }
    }
}
