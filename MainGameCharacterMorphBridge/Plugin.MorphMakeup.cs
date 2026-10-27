using System;
using UnityEngine;

namespace MainGameCharacterMorphBridge
{
    /// <summary>
    /// 化粧とペイントの補間。
    ///
    /// 化粧は chaFile.custom.face ではなく ChaFileMakeup が持つ。ゲームは
    ///   nowCoordinate.enableMakeup ? nowCoordinate.makeup : fileFace.baseMakeup
    /// で入れ物を選ぶ（CreateFaceTexture 3493行目）。こちらも同じ選び方をする。
    ///
    /// 色とレイアウト(Vector4)は混ぜられるが、ID(テクスチャ番号)は混ぜられない。
    /// 肌IDと同じく、経過時間の真ん中で切り替える。イージングを変えても瞬間がずれない。
    ///
    /// IDやレイアウトを変えた時は、色フラグだけでは焼き直されない。
    /// AddUpdateCM*TexFlags / AddUpdateCM*LayoutFlags も立てる必要がある。
    /// </summary>
    public sealed partial class Plugin
    {
        // ChaFileMakeup が持つ色。
        private static readonly string[] MakeupColorMembers =
            { "eyeshadowColor", "cheekColor", "lipColor" };

        // ChaFileMakeup が持つテクスチャ番号。順番は上の色と対応させる。
        private static readonly string[] MakeupIdMembers =
            { "eyeshadowId", "cheekId", "lipId" };

        // ChaFileFace 側の、化粧まわりで取りこぼしていた色と番号。
        private static readonly string[] FaceExtraColorMembers = { "lipLineColor", "moleColor" };
        private static readonly string[] FaceExtraIdMembers = { "lipLineId", "moleId" };

        // ChaFileBody 側の番号。日焼け柄とペイント柄。
        private static readonly string[] BodyIdMembers = { "sunburnId" };

        private bool _makeupSupportLogged;

        /// <summary>ゲームと同じ規則で化粧の入れ物を選ぶ。</summary>
        private static object ResolveMakeup(ChaFileControl file, int coordinateIndex)
        {
            if (file == null || file.custom == null)
                return null;

            var coordinates = GetMemberValue(file, "coordinate") as Array;
            if (coordinates != null && coordinateIndex >= 0 && coordinateIndex < coordinates.Length)
            {
                object coordinate = coordinates.GetValue(coordinateIndex);
                if (coordinate != null)
                {
                    object enable = GetMemberValue(coordinate, "enableMakeup");
                    if (enable is bool && (bool)enable)
                    {
                        object makeup = GetMemberValue(coordinate, "makeup");
                        if (makeup != null)
                            return makeup;
                    }
                }
            }

            return GetMemberValue(file.custom.face, "baseMakeup");
        }

        /// <summary>今の着替え番号。カード側の化粧もこの番号で引く。</summary>
        private static int ResolveCoordinateIndex(ChaControl target)
        {
            if (target == null || target.chaFile == null)
                return -1;

            object status = GetMemberValue(target.chaFile, "status");
            int index;
            return TryGetInt(status, "coordinateType", out index) ? index : -1;
        }

        // ---------------- スナップショット ----------------

        internal sealed class MakeupSnapshot
        {
            internal Color[] Colors;        // MakeupColorMembers
            internal int[] Ids;             // MakeupIdMembers
            internal Color[] PaintColors;   // makeup.paintColor
            internal int[] PaintIds;        // makeup.paintId
            internal Vector4[] PaintLayouts;// makeup.paintLayout

            internal Color[] FaceExtraColors;
            internal int[] FaceExtraIds;

            internal Color[] BodyPaintColors;
            internal int[] BodyPaintIds;
            internal int[] BodyPaintLayoutIds;
            internal Vector4[] BodyPaintLayouts;
            internal int[] BodyIds;
        }

        internal static MakeupSnapshot MakeupSnapshotFrom(ChaFileControl file, int coordinateIndex)
        {
            if (file == null || file.custom == null)
                return null;

            object makeup = ResolveMakeup(file, coordinateIndex);
            object face = file.custom.face;
            object body = file.custom.body;

            return new MakeupSnapshot
            {
                Colors = ReadColors(makeup, MakeupColorMembers),
                Ids = ReadInts(makeup, MakeupIdMembers),
                PaintColors = ReadColorArray(makeup, "paintColor"),
                PaintIds = ReadIntArray(makeup, "paintId"),
                PaintLayouts = ReadVector4Array(makeup, "paintLayout"),

                FaceExtraColors = ReadColors(face, FaceExtraColorMembers),
                FaceExtraIds = ReadInts(face, FaceExtraIdMembers),

                BodyPaintColors = ReadColorArray(body, "paintColor"),
                BodyPaintIds = ReadIntArray(body, "paintId"),
                BodyPaintLayoutIds = ReadIntArray(body, "paintLayoutId"),
                BodyPaintLayouts = ReadVector4Array(body, "paintLayout"),
                BodyIds = ReadInts(body, BodyIdMembers)
            };
        }

        // ---------------- 反映 ----------------

        /// <summary>
        /// 化粧とペイントを反映する。戻り値は「何を焼き直す必要があるか」。
        /// </summary>
        private void ApplyMakeupBlend(
            ChaControl target,
            MakeupSnapshot fromMakeup,
            MakeupSnapshot toMakeup,
            float blend,
            bool force,
            out bool faceColorChanged,
            out bool faceTexChanged,
            out bool faceLayoutChanged,
            out bool bodyColorChanged,
            out bool bodyTexChanged,
            out bool bodyLayoutChanged)
        {
            faceColorChanged = faceTexChanged = faceLayoutChanged = false;
            bodyColorChanged = bodyTexChanged = bodyLayoutChanged = false;

            if (fromMakeup == null || toMakeup == null || target == null || target.chaFile == null)
                return;

            // ID切り替えは経過時間の真ん中で行う。イージング後の値では瞬間がずれる。
            float ratio = _transitionTimeRatio >= 0f ? _transitionTimeRatio : blend;
            bool useTarget = ratio >= 0.5f;

            int coordinateIndex = ResolveCoordinateIndex(target);
            object makeup = ResolveMakeup(target.chaFile, coordinateIndex);
            object face = target.chaFile.custom != null ? target.chaFile.custom.face : null;
            object body = target.chaFile.custom != null ? target.chaFile.custom.body : null;

            // --- 化粧（顔） ---
            faceColorChanged |= ApplyContainerColors(
                makeup, MakeupColorMembers, fromMakeup.Colors, toMakeup.Colors, blend, force);
            faceTexChanged |= ApplyIds(
                makeup, MakeupIdMembers, fromMakeup.Ids, toMakeup.Ids, useTarget);

            faceColorChanged |= ApplyColorArray(
                makeup, "paintColor", fromMakeup.PaintColors, toMakeup.PaintColors, blend);
            faceTexChanged |= ApplyIdArray(
                makeup, "paintId", fromMakeup.PaintIds, toMakeup.PaintIds, useTarget);
            faceLayoutChanged |= ApplyVector4Array(
                makeup, "paintLayout", fromMakeup.PaintLayouts, toMakeup.PaintLayouts, blend);

            // --- 顔本体側の取りこぼし（リップライン・ホクロ） ---
            faceColorChanged |= ApplyContainerColors(
                face, FaceExtraColorMembers, fromMakeup.FaceExtraColors, toMakeup.FaceExtraColors, blend, force);
            faceTexChanged |= ApplyIds(
                face, FaceExtraIdMembers, fromMakeup.FaceExtraIds, toMakeup.FaceExtraIds, useTarget);

            // --- 体のペイントと日焼け柄 ---
            bodyColorChanged |= ApplyColorArray(
                body, "paintColor", fromMakeup.BodyPaintColors, toMakeup.BodyPaintColors, blend);
            bodyTexChanged |= ApplyIdArray(
                body, "paintId", fromMakeup.BodyPaintIds, toMakeup.BodyPaintIds, useTarget);
            bodyTexChanged |= ApplyIdArray(
                body, "paintLayoutId", fromMakeup.BodyPaintLayoutIds, toMakeup.BodyPaintLayoutIds, useTarget);
            bodyLayoutChanged |= ApplyVector4Array(
                body, "paintLayout", fromMakeup.BodyPaintLayouts, toMakeup.BodyPaintLayouts, blend);
            bodyTexChanged |= ApplyIds(
                body, BodyIdMembers, fromMakeup.BodyIds, toMakeup.BodyIds, useTarget);

            LogMakeupSupportOnce(makeup, fromMakeup, toMakeup);
        }

        /// <summary>何が読めて何が読めなかったかを一度だけ残す。名前違いを黙って素通りさせないため。</summary>
        private void LogMakeupSupportOnce(object makeup, MakeupSnapshot from, MakeupSnapshot to)
        {
            if (_makeupSupportLogged)
                return;
            _makeupSupportLogged = true;

            LogExecution("makeup support: container=" + (makeup != null ? makeup.GetType().Name : "null")
                + " colors=" + (from.Colors != null ? from.Colors.Length : 0)
                + " ids=" + (from.Ids != null ? from.Ids.Length : 0)
                + " facePaint=" + (from.PaintColors != null ? from.PaintColors.Length : 0)
                + " bodyPaint=" + (from.BodyPaintColors != null ? from.BodyPaintColors.Length : 0));

            if (from.Colors != null && to.Colors != null)
            {
                for (int i = 0; i < from.Colors.Length && i < to.Colors.Length; i++)
                {
                    Color f = from.Colors[i];
                    Color t = to.Colors[i];
                    LogExecution("makeup " + MakeupColorMembers[i]
                        + " from=" + f.r.ToString("0.00") + "," + f.g.ToString("0.00") + "," + f.b.ToString("0.00")
                        + " to=" + t.r.ToString("0.00") + "," + t.g.ToString("0.00") + "," + t.b.ToString("0.00")
                        + (f == t ? " (同じ色)" : string.Empty));
                }
            }
        }

        // ---------------- 配列・ID用の補助 ----------------

        private static bool ApplyIds(object owner, string[] members, int[] from, int[] to, bool useTarget)
        {
            if (owner == null || from == null || to == null)
                return false;

            bool changed = false;
            int count = Mathf.Min(members.Length, Mathf.Min(from.Length, to.Length));
            for (int i = 0; i < count; i++)
            {
                if (from[i] == to[i])
                    continue;

                int wanted = useTarget ? to[i] : from[i];
                int current;
                if (TryGetInt(owner, members[i], out current) && current == wanted)
                    continue;

                changed |= TrySetInt(owner, members[i], wanted);
            }
            return changed;
        }

        private static int[] ReadInts(object owner, string[] members)
        {
            if (owner == null)
                return null;

            var values = new int[members.Length];
            for (int i = 0; i < members.Length; i++)
            {
                int value;
                values[i] = TryGetInt(owner, members[i], out value) ? value : 0;
            }
            return values;
        }

        private static Color[] ReadColorArray(object owner, string name)
        {
            var array = GetMemberValue(owner, name) as Color[];
            if (array == null)
                return null;

            var copy = new Color[array.Length];
            Array.Copy(array, copy, array.Length);
            return copy;
        }

        private static int[] ReadIntArray(object owner, string name)
        {
            var array = GetMemberValue(owner, name) as int[];
            if (array == null)
                return null;

            var copy = new int[array.Length];
            Array.Copy(array, copy, array.Length);
            return copy;
        }

        private static Vector4[] ReadVector4Array(object owner, string name)
        {
            var array = GetMemberValue(owner, name) as Vector4[];
            if (array == null)
                return null;

            var copy = new Vector4[array.Length];
            Array.Copy(array, copy, array.Length);
            return copy;
        }

        private static bool ApplyColorArray(object owner, string name, Color[] from, Color[] to, float blend)
        {
            var live = GetMemberValue(owner, name) as Color[];
            if (live == null || from == null || to == null)
                return false;

            bool changed = false;
            int count = Mathf.Min(live.Length, Mathf.Min(from.Length, to.Length));
            for (int i = 0; i < count; i++)
            {
                if (from[i] == to[i])
                    continue;

                live[i] = Color.Lerp(from[i], to[i], blend);
                changed = true;
            }
            return changed;
        }

        private static bool ApplyIdArray(object owner, string name, int[] from, int[] to, bool useTarget)
        {
            var live = GetMemberValue(owner, name) as int[];
            if (live == null || from == null || to == null)
                return false;

            bool changed = false;
            int count = Mathf.Min(live.Length, Mathf.Min(from.Length, to.Length));
            for (int i = 0; i < count; i++)
            {
                if (from[i] == to[i])
                    continue;

                int wanted = useTarget ? to[i] : from[i];
                if (live[i] == wanted)
                    continue;

                live[i] = wanted;
                changed = true;
            }
            return changed;
        }

        private static bool ApplyVector4Array(object owner, string name, Vector4[] from, Vector4[] to, float blend)
        {
            var live = GetMemberValue(owner, name) as Vector4[];
            if (live == null || from == null || to == null)
                return false;

            bool changed = false;
            int count = Mathf.Min(live.Length, Mathf.Min(from.Length, to.Length));
            for (int i = 0; i < count; i++)
            {
                if (from[i] == to[i])
                    continue;

                live[i] = Vector4.Lerp(from[i], to[i], blend);
                changed = true;
            }
            return changed;
        }
    }
}
