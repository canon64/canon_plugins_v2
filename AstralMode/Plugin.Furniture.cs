using System;
using System.Collections.Generic;
using UnityEngine;

namespace AstralMode
{
    /// <summary>
    /// アストラル界に置く家具（ベンチ・テーブル・ベッド）。
    ///
    /// 外部モデルは使わず、板と脚の組み合わせでコード生成する。
    /// 丘・草と同じ理由で、外から持ち込んだマテリアルが剥がれる事故を最初から起こさないため。
    ///
    /// アストラルの開始体位はベンチフェラ固定（CONCEPT.md）なのに座るベンチが世界に無かった。
    /// ここで実体を用意する。寸法は preview/furniture.png で確認した値と同じ。
    ///
    /// 位置・角度・サイズは家具ごとに Config Manager から個別に変更できる。
    /// </summary>
    public sealed partial class Plugin
    {
        /// <summary>家具の部材1つ。どの材質で塗るかだけを持つ直方体。</summary>
        private struct FurniturePart
        {
            internal Vector3 Centre;
            internal Vector3 Size;
            internal int Material;      // 0=明るい木 1=濃い木 2=マットレス 3=枕 4=掛け布団

            internal FurniturePart(float cx, float cy, float cz, float sx, float sy, float sz, int material)
            {
                Centre = new Vector3(cx, cy, cz);
                Size = new Vector3(sx, sy, sz);
                Material = material;
            }
        }

        private const int MatWoodLight = 0;
        private const int MatWoodDark = 1;
        private const int MatFabric = 2;
        private const int MatPillow = 3;
        private const int MatBlanket = 4;

        // ベッドのフレーム底からマットレス上面までの高さ。挿入体位の追従で高さを合わせるのに使う。
        internal const float BedRailTop = 0.36f;
        internal const float BedMattressThickness = 0.26f;
        internal const float BedMattressTopLocal = BedRailTop + BedMattressThickness;

        /// <summary>1台ぶんの据え付け情報。草を刈る範囲もここから決まる。</summary>
        private struct FurniturePlacement
        {
            internal string Name;
            internal bool Enabled;
            internal Vector3 LocalPosition;     // 丘ローカル。Y は地面からの持ち上げ量
            internal Vector3 EulerAngles;
            internal float Scale;
            internal Vector2 FootprintHalf;     // 草を刈る範囲（スケール適用前の半径）
            internal bool ClearingOnly;         // true なら草を刈るだけで家具は生やさない
        }

        // ------------------------------------------------------------------
        // 部材の定義
        // ------------------------------------------------------------------

        /// <summary>公園のベンチ。座面と背もたれは板を並べる（一枚板だと安っぽく見える）。</summary>
        private static List<FurniturePart> BuildBenchParts()
        {
            const float width = 1.70f;
            const float depth = 0.46f;
            const float seatH = 0.45f;
            const float slat = 0.035f;

            List<FurniturePart> p = new List<FurniturePart>();

            // 座面：板3枚
            for (int i = 0; i < 3; i++)
            {
                float off = Mathf.Lerp(-depth * 0.34f, depth * 0.34f, i / 2f);
                p.Add(new FurniturePart(0f, seatH, off, width, slat, depth * 0.26f, MatWoodLight));
            }

            // 背もたれ：板2枚を後ろへ倒して付ける
            float[] backHeights = { seatH + 0.26f, seatH + 0.43f };
            for (int i = 0; i < backHeights.Length; i++)
            {
                float h = backHeights[i];
                float lean = -depth * 0.40f - (h - seatH) * 0.16f;
                p.Add(new FurniturePart(0f, h, lean, width, depth * 0.22f, slat, MatWoodLight));
            }

            // 脚・側枠・背もたれの支柱
            float[] sides = { -width * 0.42f, width * 0.42f };
            for (int i = 0; i < sides.Length; i++)
            {
                float sx = sides[i];
                p.Add(new FurniturePart(sx, seatH * 0.5f, -depth * 0.34f, 0.07f, seatH, 0.07f, MatWoodDark));
                p.Add(new FurniturePart(sx, seatH * 0.5f, depth * 0.34f, 0.07f, seatH, 0.07f, MatWoodDark));
                p.Add(new FurniturePart(sx, seatH - 0.04f, 0f, 0.055f, 0.05f, depth, MatWoodDark));
                p.Add(new FurniturePart(sx, seatH + 0.24f, -depth * 0.44f, 0.06f, 0.50f, 0.06f, MatWoodDark));
            }

            return p;
        }

        /// <summary>ベンチと幅・材質・脚の太さを揃えたテーブル。脚を入れられる奥行きを取る。</summary>
        private static List<FurniturePart> BuildTableParts()
        {
            const float width = 1.70f;
            const float depth = 0.78f;
            const float topH = 0.72f;
            const float slat = 0.045f;

            List<FurniturePart> p = new List<FurniturePart>();

            for (int i = 0; i < 4; i++)                       // 天板：板4枚
            {
                float off = Mathf.Lerp(-depth * 0.36f, depth * 0.36f, i / 3f);
                p.Add(new FurniturePart(0f, topH, off, width, slat, depth * 0.21f, MatWoodLight));
            }

            float[] sides = { -width * 0.40f, width * 0.40f };
            for (int i = 0; i < sides.Length; i++)
            {
                float sx = sides[i];
                p.Add(new FurniturePart(sx, topH * 0.5f, -depth * 0.32f, 0.075f, topH, 0.075f, MatWoodDark));
                p.Add(new FurniturePart(sx, topH * 0.5f, depth * 0.32f, 0.075f, topH, 0.075f, MatWoodDark));
                p.Add(new FurniturePart(sx, topH - 0.09f, 0f, 0.06f, 0.055f, depth * 0.72f, MatWoodDark));
            }

            p.Add(new FurniturePart(0f, 0.16f, 0f, width * 0.72f, 0.055f, 0.06f, MatWoodDark));  // 貫
            return p;
        }

        /// <summary>
        /// 木枠ベッド。枠は板一枚ではなく側板4枚で囲う。
        /// 板一枚の上にマットレスを載せると、マットレスが枠に沈んだ「浅い箱に板を敷いた」形に見える。
        /// マットレスは枠より高く盛り上げ、掛け布団を足側に掛ける。布団が無いと寝具に見えない。
        /// </summary>
        private static List<FurniturePart> BuildBedParts()
        {
            const float width = 1.45f;
            const float length = 2.05f;
            const float railTop = BedRailTop;         // 枠の上端
            const float matH = BedMattressThickness;  // マットレスの厚み
            const float matTop = BedMattressTopLocal;

            List<FurniturePart> p = new List<FurniturePart>();

            // 枠：側板4枚で囲う
            p.Add(new FurniturePart(0f, railTop - 0.09f, -length * 0.5f + 0.035f, width, 0.18f, 0.07f, MatWoodDark));
            p.Add(new FurniturePart(0f, railTop - 0.09f, length * 0.5f - 0.035f, width, 0.18f, 0.07f, MatWoodDark));
            p.Add(new FurniturePart(-width * 0.5f + 0.035f, railTop - 0.09f, 0f, 0.07f, 0.18f, length, MatWoodDark));
            p.Add(new FurniturePart(width * 0.5f - 0.035f, railTop - 0.09f, 0f, 0.07f, 0.18f, length, MatWoodDark));

            // マットレス：枠の上に載っているように見せる
            p.Add(new FurniturePart(0f, railTop + matH * 0.5f, 0f, width - 0.06f, matH, length - 0.10f, MatFabric));

            // 掛け布団：足側を覆い、両脇に少し垂らす。手前の縁はめくり返す
            p.Add(new FurniturePart(0f, matTop + 0.035f, length * 0.20f, width + 0.04f, 0.09f, length * 0.56f, MatBlanket));
            p.Add(new FurniturePart(0f, matTop - 0.02f, length * 0.20f - length * 0.28f + 0.02f, width + 0.04f, 0.10f, 0.06f, MatBlanket));

            // 枕：マットレスの上に載せる
            float[] pillowX = { -width * 0.24f, width * 0.24f };
            for (int i = 0; i < pillowX.Length; i++)
                p.Add(new FurniturePart(pillowX[i], matTop + 0.075f, -length * 0.34f, width * 0.42f, 0.15f, 0.34f, MatPillow));

            // 頭板・足板
            p.Add(new FurniturePart(0f, 0.70f, -length * 0.5f - 0.02f, width + 0.06f, 1.00f, 0.07f, MatWoodLight));
            p.Add(new FurniturePart(0f, 0.33f, length * 0.5f + 0.02f, width + 0.06f, 0.62f, 0.07f, MatWoodLight));

            // 脚
            float[] legX = { -width * 0.44f, width * 0.44f };
            float[] legZ = { -length * 0.44f, length * 0.44f };
            for (int i = 0; i < legX.Length; i++)
                for (int j = 0; j < legZ.Length; j++)
                    p.Add(new FurniturePart(legX[i], 0.11f, legZ[j], 0.10f, 0.22f, 0.10f, MatWoodDark));

            return p;
        }

        // ------------------------------------------------------------------
        // 据え付け
        // ------------------------------------------------------------------

        /// <summary>Config から3台ぶんの据え付け情報を読む。草を刈る前に必要なので生成より先に呼ぶ。</summary>
        private List<FurniturePlacement> ReadFurniturePlacements()
        {
            List<FurniturePlacement> list = new List<FurniturePlacement>();

            list.Add(new FurniturePlacement
            {
                Name = "Bench",
                Enabled = _cfgUseAstralBench == null || _cfgUseAstralBench.Value,
                LocalPosition = ReadVector(_cfgAstralBenchPosX, _cfgAstralBenchPosY, _cfgAstralBenchPosZ),
                EulerAngles = ReadVector(_cfgAstralBenchRotX, _cfgAstralBenchRotY, _cfgAstralBenchRotZ),
                Scale = ReadScale(_cfgAstralBenchScale),
                FootprintHalf = new Vector2(0.95f, 0.42f)
            });

            list.Add(new FurniturePlacement
            {
                Name = "Table",
                Enabled = _cfgUseAstralTable == null || _cfgUseAstralTable.Value,
                LocalPosition = ReadVector(_cfgAstralTablePosX, _cfgAstralTablePosY, _cfgAstralTablePosZ),
                EulerAngles = ReadVector(_cfgAstralTableRotX, _cfgAstralTableRotY, _cfgAstralTableRotZ),
                Scale = ReadScale(_cfgAstralTableScale),
                FootprintHalf = new Vector2(0.95f, 0.52f)
            });

            list.Add(new FurniturePlacement
            {
                Name = "Bed",
                Enabled = _cfgUseAstralBed == null || _cfgUseAstralBed.Value,
                LocalPosition = ReadVector(_cfgAstralBedPosX, _cfgAstralBedPosY, _cfgAstralBedPosZ),
                EulerAngles = ReadVector(_cfgAstralBedRotX, _cfgAstralBedRotY, _cfgAstralBedRotZ),
                Scale = ReadScale(_cfgAstralBedScale),
                FootprintHalf = new Vector2(0.82f, 1.12f)
            });

            // 挿入体位でベッドがキャラの下へ移動する先（＝丘の中心）を、あらかじめ刈っておく。
            // 追従は Transform を動かすだけで組み直さないため、ここを刈っておかないと
            // ベッドを草が突き抜ける。
            if (_cfgBedFollowOnInsertion != null && _cfgBedFollowOnInsertion.Value)
            {
                list.Add(new FurniturePlacement
                {
                    Name = "BedFollowClearing",
                    Enabled = true,
                    ClearingOnly = true,
                    LocalPosition = new Vector3(
                        -(_cfgAstralHillPosX != null ? _cfgAstralHillPosX.Value : 0f),
                        0f,
                        -(_cfgAstralHillPosZ != null ? _cfgAstralHillPosZ.Value : 0f)),
                    EulerAngles = Vector3.zero,
                    Scale = ReadScale(_cfgAstralBedScale),
                    // 向きはキャラ次第で回るので、どの向きでも収まるよう長辺で正方に取る
                    FootprintHalf = new Vector2(1.12f, 1.12f)
                });
            }

            return list;
        }

        private static Vector3 ReadVector(BepInEx.Configuration.ConfigEntry<float> x,
                                          BepInEx.Configuration.ConfigEntry<float> y,
                                          BepInEx.Configuration.ConfigEntry<float> z)
        {
            return new Vector3(x != null ? x.Value : 0f,
                               y != null ? y.Value : 0f,
                               z != null ? z.Value : 0f);
        }

        private static float ReadScale(BepInEx.Configuration.ConfigEntry<float> entry)
        {
            return Mathf.Max(0.01f, entry != null ? entry.Value : 1f);
        }

        /// <summary>
        /// 家具の足元かどうか。true なら草を生やさない。
        /// 刈る範囲は家具の位置・角度・サイズに連動するので、動かせば刈る場所も一緒に動く。
        /// </summary>
        private bool IsUnderFurniture(List<FurniturePlacement> placements, float x, float z)
        {
            for (int i = 0; i < placements.Count; i++)
            {
                FurniturePlacement f = placements[i];
                if (!f.Enabled) continue;

                float dx = x - f.LocalPosition.x;
                float dz = z - f.LocalPosition.z;

                // 家具のローカル向きへ引き戻してから矩形で判定する
                float rad = -f.EulerAngles.y * Mathf.Deg2Rad;
                float cos = Mathf.Cos(rad);
                float sin = Mathf.Sin(rad);
                float lx = dx * cos - dz * sin;
                float lz = dx * sin + dz * cos;

                if (Mathf.Abs(lx) < f.FootprintHalf.x * f.Scale &&
                    Mathf.Abs(lz) < f.FootprintHalf.y * f.Scale)
                    return true;
            }
            return false;
        }

        /// <summary>家具3台を生成して親にぶら下げる。</summary>
        private void BuildFurniture(Transform parent, List<FurniturePlacement> placements, Material[] materials)
        {
            for (int i = 0; i < placements.Count; i++)
            {
                FurniturePlacement f = placements[i];
                if (!f.Enabled || f.ClearingOnly) continue;

                List<FurniturePart> parts;
                if (f.Name == "Bench") parts = BuildBenchParts();
                else if (f.Name == "Table") parts = BuildTableParts();
                else parts = BuildBedParts();

                GameObject root = new GameObject("AstralMode_" + f.Name);
                root.transform.SetParent(parent, false);
                root.hideFlags = HideFlags.DontSave;

                // 地面の上に自動で乗せる。Config の Y はそこからの持ち上げ量として効く。
                float groundY = HillHeight(f.LocalPosition.x, f.LocalPosition.z);
                root.transform.localPosition = new Vector3(f.LocalPosition.x,
                                                           groundY + f.LocalPosition.y,
                                                           f.LocalPosition.z);
                root.transform.localRotation = Quaternion.Euler(f.EulerAngles);
                root.transform.localScale = Vector3.one * f.Scale;

                // 材質ごとにまとめて1枚のメッシュにする
                for (int m = 0; m < materials.Length; m++)
                {
                    MeshBuilder mb = new MeshBuilder();
                    int used = 0;
                    for (int k = 0; k < parts.Count; k++)
                    {
                        if (parts[k].Material != m) continue;
                        AppendBox(mb, parts[k].Centre, parts[k].Size);
                        used++;
                    }
                    if (used == 0) continue;
                    Mesh mesh = mb.ToMesh("Astral" + f.Name + "_" + m);
                    _astralOwnedMeshes.Add(mesh);      // 組み直しで確実に捨てるため控えておく
                    CreateMeshObject(f.Name + "_Mat" + m, root.transform, mesh, materials[m]);
                }

                _astralFurnitureRoots.Add(root);
                _astralFurnitureByName[f.Name] = root;   // スライダーでその場で動かすため名前で引けるようにする
                SimpleFileLogger.Info($"Astral furniture placed: {f.Name} local={root.transform.localPosition} "
                    + $"rot={f.EulerAngles} scale={f.Scale:F3} parts={parts.Count}");
            }
        }
    }
}
