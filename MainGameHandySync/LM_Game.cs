// LoveMachine (Sauceke, GPLv3) からの移植。
// 元: Game/GameAdapter.cs + LoveMachine.KK/KoikatsuGame.cs + AbstractKoikatsuGame.cs
// KKS 用に、当プラグインの HFlag から男女ボーン・animState・pose を供給する。
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using UnityEngine;

namespace MainGameHandySync.LM
{
    internal sealed class LmGame
    {
        // 当プラグイン側から毎フレ設定される
        public HFlag Flags { get; set; }

        private ObjectIDGenerator _idGenerator = new ObjectIDGenerator();

        public int AnimationLayer => 0;
        public float PenisSize => 0.1f;
        public float MinStrokeLength => 0.5f;
        public float StrokingIntensity => 0f;

        private static readonly HFlag.EMode[] playerlessModes =
            { HFlag.EMode.lesbian, HFlag.EMode.masturbation };

        private static readonly HFlag.EMode[] supportedModes = new[]
        {
            HFlag.EMode.houshi, HFlag.EMode.sonyu, HFlag.EMode.houshi3P, HFlag.EMode.sonyu3P,
            HFlag.EMode.houshi3PMMF, HFlag.EMode.sonyu3PMMF
        }.Concat(playerlessModes).ToArray();

        private static readonly string[] activeAnimations =
        {
            "WLoop", "MLoop", "SLoop", "A_WLoop", "A_SLoop", "A_OLoop", "OLoop"
        };

        // AbstractKoikatsuGame.FemaleBoneNames
        public readonly Dictionary<Bone, string> FemaleBoneNames = new Dictionary<Bone, string>
        {
            { Bone.LeftBreast, "k_f_munenipL_00" },
            { Bone.RightBreast, "k_f_munenipR_00" },
            { Bone.Vagina, "cf_n_pee" },
            { Bone.Anus, "k_f_ana_00" },
            { Bone.LeftButt, "k_f_siriL_00" },
            { Bone.RightButt, "k_f_siriR_00" },
            { Bone.Mouth, "cf_J_MouthCavity" },
            { Bone.LeftHand, "cf_j_index04_L" },
            { Bone.RightHand, "cf_j_index04_R" },
            { Bone.LeftFoot, "k_f_toeL_00" },
            { Bone.RightFoot, "k_f_toeR_00" },
        };

        public Animator GetFemaleAnimator(int girlIndex) =>
            Flags.lstHeroine[girlIndex].chaCtrl.animBody;

        public GameObject GetFemaleRoot(int girlIndex) =>
            Flags.lstHeroine[girlIndex].chaCtrl.objBodyBone;

        // KoikatsuGame.PenisBases（sonyu/houshi 系：男の玉ボーン）
        public Transform[] PenisBases
        {
            get
            {
                switch (Flags.mode)
                {
                    case HFlag.EMode.masturbation:
                        return new[] { FindBoneByPath(GetFemaleRoot(0), FemaleBoneNames[Bone.RightHand]) }
                            .Where(t => t != null).ToArray();
                    case HFlag.EMode.lesbian:
                        var root = GetFemaleRoot(1);
                        return new[] { Bone.Vagina, Bone.RightHand, Bone.Mouth }
                            .Select(b => FindBoneByPath(root, FemaleBoneNames[b]))
                            .Where(t => t != null).ToArray();
                    default:
                        var pRoot = Flags.player.chaCtrl.objBodyBone;
                        return new[] { FindBoneByPath(pRoot, "k_f_tamaL_00") }
                            .Where(t => t != null).ToArray();
                }
            }
        }

        public bool IsIdle(int girlIndex) => !supportedModes.Contains(Flags.mode)
            || !activeAnimations.Contains(Flags.nowAnimStateName)
            || (Flags.speed < 1f && !playerlessModes.Contains(Flags.mode));

        public string GetPose(int girlIndex) =>
            _idGenerator.GetId(Flags.nowAnimationInfo, out _)
                + "." + Flags.nowAnimationInfo.nameAnimation
                + "." + Flags.nowAnimStateName;

        public IEnumerator WaitAfterPoseChange()
        {
            while (!GetAnimatorStateInfo(0).IsName(Flags.nowAnimStateName))
                yield return new WaitForSeconds(0.1f);
            yield return new WaitForSeconds(0.1f);
        }

        // ===== GameAdapter から（ほぼ verbatim）=====

        public AnimatorStateInfo GetAnimatorStateInfo(int girlIndex) =>
            GetFemaleAnimator(girlIndex).GetCurrentAnimatorStateInfo(AnimationLayer);

        public void GetAnimState(int girlIndex, out float normalizedTime, out float length, out float speed)
        {
            var info = GetAnimatorStateInfo(girlIndex);
            normalizedTime = info.normalizedTime;
            length = info.length;
            speed = 1f;
        }

        // 丸める前の生値を出すための間引きログ。全送信が t=1ms に潰れている原因が
        // length なのか timeScale なのかを、推測ではなく実測で分けるために置く。
        private static float _animTimeLogNext;

        public float GetAnimationTimeSecs(int girlIndex)
        {
            GetAnimState(girlIndex, out _, out float length, out float speed);
            float animTimeSecs = length / speed / Time.timeScale;
            bool clamped = animTimeSecs > 10000f || animTimeSecs < 0.001f || float.IsNaN(animTimeSecs);

            if (Plugin.Instance != null && Plugin.Instance.ShouldLogNormal() && Time.unscaledTime >= _animTimeLogNext)
            {
                _animTimeLogNext = Time.unscaledTime + 2f;
                SimpleFileLogger.Info($"[LM] animTime raw: length={length:0.####} speed={speed:0.###}"
                    + $" timeScale={Time.timeScale:0.###} -> {animTimeSecs:0.####}"
                    + (clamped ? " CLAMPED to 0.01" : ""));
            }

            return clamped ? .01f : animTimeSecs;
        }

        public Dictionary<Bone, Transform> GetFemaleBones(int girlIndex) =>
            FemaleBoneNames.ToDictionary(kvp => kvp.Key,
                kvp => FindBoneByPath(GetFemaleRoot(girlIndex), kvp.Value));

        public static Transform FindBoneByPath(GameObject root, string path) =>
            root?.transform.Find(path)
                ?? FindDeepChildrenByPath(root, path).FirstOrDefault()
                    ?? FindDeepChildrenByPath(root, path.Split('/').Last()).FirstOrDefault()
                        ?? GameObject.Find(path.Split('/').Last())?.transform;

        private static Transform[] FindDeepChildrenByPath(GameObject root, string path)
        {
            var pathFragments = path.Split('/').Reverse().ToArray();
            return root?
                .GetComponentsInChildren<Transform>()
                .Where(child => HasPath(child, pathFragments))
                .ToArray() ?? new Transform[] { };
        }

        private static bool HasPath(Transform tf, string[] path, int index = 0) =>
            index == path.Length || (tf?.name == path[index] && HasPath(tf?.parent, path, ++index));
    }
}
