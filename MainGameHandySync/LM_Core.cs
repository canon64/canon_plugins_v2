// LoveMachine (Sauceke, GPLv3) からの移植。出力層のみ v3 HDSP に差し替え。
// 元: LoveMachine.Core/Common/* および Game/StrokeInfo.cs, TrackingKey.cs
using System;
using System.Collections.Generic;

namespace MainGameHandySync.LM
{
    public enum Bone
    {
        Auto, Vagina, Anus, Mouth, LeftHand, RightHand, LeftFoot, RightFoot,
        LeftBreast, RightBreast, LeftButt, RightButt
    }

    public enum Axis { Longest, X, Y, Z }

    public enum MovementType { Linear, Rotation }

    public enum POV { Balanced, Male, Female }

    public enum StrokingPattern { Sine, Cups, Arches, Animation, Custom }

    public struct Node
    {
        public float Time { get; set; }
        public float Position { get; set; }
    }

    public struct StrokeInfo
    {
        public float Amplitude { get; set; }
        public float DurationSecs { get; set; }
        public float Completion { get; set; }
        public float[] Pattern { get; set; }
    }

    public struct TrackingKey
    {
        public int GirlIndex { get; set; }
        public Bone Bone { get; set; }
        public string Pose { get; set; }
        public POV POV { get; set; }
        public Axis Axis { get; set; }
        public MovementType MovementType { get; set; }
    }

    public static class LinqExtensions
    {
        public static T Maximize<T>(this IEnumerable<T> items, Func<T, float> keySelector)
        {
            float max = float.NegativeInfinity;
            T argMax = default;
            foreach (var item in items)
            {
                float value = keySelector(item);
                if (max <= value)
                {
                    argMax = item;
                    max = value;
                }
            }
            return argMax;
        }

        public static T Minimize<T>(this IEnumerable<T> items, Func<T, float> keySelector) =>
            Maximize(items, item => -keySelector(item));
    }
}
