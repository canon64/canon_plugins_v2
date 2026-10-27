using System;
using UnityEngine;

namespace MainGameObjectComposer
{
    internal sealed class MovementField
    {
        internal string Key, Label;
        internal Func<ManagedObjectData, float> Read;
        internal Action<ManagedObjectData, float> Write;
        internal bool Radius, Angle;
        internal MovementField(string key, string label, Func<ManagedObjectData, float> read,
            Action<ManagedObjectData, float> write, bool radius = false, bool angle = false)
        { Key = key; Label = label; Read = read; Write = write; Radius = radius; Angle = angle; }

        internal float Blend(float a, float b, float t) => Angle ? Mathf.LerpAngle(a, b, t) : Mathf.Lerp(a, b, t);
        internal bool Different(float a, float b) => Mathf.Abs(Angle ? Mathf.DeltaAngle(a, b) : a - b) > 0.00001f;

        internal static readonly MovementField[] All = {
            new MovementField("px", "位置X", d=>d.localPosition.x, (d,v)=>d.localPosition.x=v),
            new MovementField("py", "位置Y", d=>d.localPosition.y, (d,v)=>d.localPosition.y=v),
            new MovementField("pz", "位置Z", d=>d.localPosition.z, (d,v)=>d.localPosition.z=v),
            new MovementField("rotX", "回転X", d=>d.localEulerAngles.x, (d,v)=>d.localEulerAngles.x=v, angle:true),
            new MovementField("rotY", "回転Y", d=>d.localEulerAngles.y, (d,v)=>d.localEulerAngles.y=v, angle:true),
            new MovementField("rotZ", "回転Z", d=>d.localEulerAngles.z, (d,v)=>d.localEulerAngles.z=v, angle:true),
            new MovementField("sx", "大きさX", d=>d.localScale.x, (d,v)=>d.localScale.x=v),
            new MovementField("sy", "大きさY", d=>d.localScale.y, (d,v)=>d.localScale.y=v),
            new MovementField("sz", "大きさZ", d=>d.localScale.z, (d,v)=>d.localScale.z=v),
            new MovementField("orbitRx", "Rx半径", d=>d.orbitRadiusX, (d,v)=>d.orbitRadiusX=v, radius:true),
            new MovementField("orbitRz", "Rz半径", d=>d.orbitRadiusZ, (d,v)=>d.orbitRadiusZ=v, radius:true),
            new MovementField("tube", "管の太さ", d=>d.tubeRadius, (d,v)=>d.tubeRadius=v),
            new MovementField("speed", "公転速度Hz", d=>d.orbitSpeedHz, (d,v)=>d.orbitSpeedHz=v),
            new MovementField("phase", "位相", d=>d.orbitPhaseTurns, (d,v)=>d.orbitPhaseTurns=v),
            new MovementField("animSpeed", "アニメ同期倍率", d=>d.animSpeedMultiplier, (d,v)=>d.animSpeedMultiplier=v),
            new MovementField("animPhase", "アニメ同期位相", d=>d.animSyncPhaseShift, (d,v)=>d.animSyncPhaseShift=v),
        };
    }
}
