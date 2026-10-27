using System.Collections.Generic;
using UnityEngine;

namespace MainGameUnifiedUi
{
    internal static class InputBlockGeometry
    {
        internal static void BuildWindowInputRects(
            Rect windowRect,
            float titleBarHeight,
            Rect contentLocalRect,
            bool hasMeasuredContentRect,
            float screenWidth,
            float screenHeight,
            List<Rect> output)
        {
            output.Clear();

            // 窓は丸ごと入力を食う。
            // 以前はタイトルバーだけを登録し、中身は「測定できていれば」追加していた。
            // 測定前の1フレームやタブ切り替え直後は中身が無防備になり、
            // スライダーを掴んだ瞬間にカメラがマウスを持って行っていた。
            AddClipped(windowRect, screenWidth, screenHeight, output);

            if (!hasMeasuredContentRect || !IsUsableRect(contentLocalRect))
                return;

            AddClipped(
                new Rect(
                    windowRect.x + contentLocalRect.x,
                    windowRect.y + contentLocalRect.y,
                    contentLocalRect.width,
                    contentLocalRect.height),
                screenWidth,
                screenHeight,
                output);
        }

        internal static bool Contains(IList<Rect> rects, Vector2 point)
        {
            if (rects == null)
                return false;

            for (int i = 0; i < rects.Count; i++)
            {
                if (rects[i].Contains(point))
                    return true;
            }

            return false;
        }

        internal static bool IsUsableRect(Rect rect)
        {
            return IsFinite(rect.x)
                && IsFinite(rect.y)
                && IsFinite(rect.width)
                && IsFinite(rect.height)
                && rect.width > 0f
                && rect.height > 0f;
        }

        private static void AddClipped(Rect rect, float screenWidth, float screenHeight, List<Rect> output)
        {
            if (!IsUsableRect(rect) || screenWidth <= 0f || screenHeight <= 0f)
                return;

            float xMin = Mathf.Clamp(rect.xMin, 0f, screenWidth);
            float xMax = Mathf.Clamp(rect.xMax, 0f, screenWidth);
            float yMin = Mathf.Clamp(rect.yMin, 0f, screenHeight);
            float yMax = Mathf.Clamp(rect.yMax, 0f, screenHeight);
            if (xMax <= xMin || yMax <= yMin)
                return;

            output.Add(Rect.MinMaxRect(xMin, yMin, xMax, yMax));
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
