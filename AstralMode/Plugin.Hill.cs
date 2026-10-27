using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace AstralMode
{
    /// <summary>
    /// アストラル界の景色（丘・草・家具）の生成と破棄。
    ///
    /// 以前は外部 AssetBundle `hill_test.unity3d` の完成品プレハブを置いていたが、
    /// 実機でマテリアルが剥がれ「丘が茶色」「草が三角ポリゴン」になっていた。
    /// バンドルを焼いた元プロジェクトも消失していたため、外部依存を全廃して
    /// すべてランタイム生成に切り替えた（IMPL_PLAN_hill_rebuild.md）。
    ///
    /// 破棄の担保は「生成物はすべて1つの親の下に置く。親を消せば必ず元通り」の一点。
    /// RenderSettings 等のグローバル設定には触らない（Plugin.AstralLight.cs の方針を踏襲）。
    /// </summary>
    public sealed partial class Plugin
    {
        private string _astralHillPluginDir;
        private GameObject _astralHillRoot;                 // 景色すべての親
        private Transform _astralHillGeometryRoot;          // 丘と草だけの親（丘の Transform 設定が効く）
        private readonly List<GameObject> _astralFurnitureRoots = new List<GameObject>();
        private readonly Dictionary<string, GameObject> _astralFurnitureByName = new Dictionary<string, GameObject>();

        // 地面テクスチャは焼くのに一番時間がかかる（100万ピクセル×十数回のノイズ）。
        // 中身を決める入力が変わらない限り使い回す。
        private Texture2D _astralGroundTexture;
        private string _astralGroundTextureKey;

        // スライダーをドラッグすると1目盛りごとに通知が来る。そのたびに組み直すと固まるので、
        // 手が止まってから一度だけ組み直す。その予約時刻。
        private float _sceneryRebuildAt = -1f;
        private const float SceneryRebuildDelay = 0.45f;

        /// <summary>自己発光の塗り替え対象。明るさスライダーを即座に効かせるために覚えておく。</summary>
        private struct SceneryFillTarget
        {
            internal Material Material;
            internal Color BaseColour;
            internal Texture2D Texture;
        }

        private readonly List<SceneryFillTarget> _sceneryFillTargets = new List<SceneryFillTarget>();
        private readonly List<Mesh> _astralOwnedMeshes = new List<Mesh>();
        private readonly List<Material> _astralOwnedMaterials = new List<Material>();
        private readonly List<Texture2D> _astralOwnedTextures = new List<Texture2D>();
        private bool _astralSceneryDirty = true;

        private const int GrassSeed = 20260805;

        private void InitAstralHill(string pluginDir)
        {
            _astralHillPluginDir = pluginDir;
            AttachAstralHillConfigHandlers();
            SimpleFileLogger.Info($"Astral scenery init (runtime generated, no AssetBundle): pluginDir={_astralHillPluginDir}");
        }

        // ------------------------------------------------------------------
        // 設定の反映
        // ------------------------------------------------------------------

        // どの設定が動いたら「置き直すだけ」で、どれが動いたら「組み直す」かの振り分け表。
        private readonly HashSet<ConfigEntryBase> _sceneryTransformEntries = new HashSet<ConfigEntryBase>();
        private readonly HashSet<ConfigEntryBase> _furnitureTransformEntries = new HashSet<ConfigEntryBase>();
        private readonly HashSet<ConfigEntryBase> _sceneryRebuildEntries = new HashSet<ConfigEntryBase>();

        private void AttachAstralHillConfigHandlers()
        {
            // 置き場所だけの変更は組み直さずに済む
            Register(_sceneryTransformEntries,
                _cfgAstralHillPosX, _cfgAstralHillPosY, _cfgAstralHillPosZ,
                _cfgAstralHillRotX, _cfgAstralHillRotY, _cfgAstralHillRotZ,
                _cfgAstralHillScale);

            // 家具の置き場所は、その場で家具だけ動かす。組み直さない。
            // 足元の草を刈り直す必要はあるが、それは手が止まってから遅れて追いつかせる。
            Register(_furnitureTransformEntries,
                _cfgAstralBenchPosX, _cfgAstralBenchPosY, _cfgAstralBenchPosZ,
                _cfgAstralBenchRotX, _cfgAstralBenchRotY, _cfgAstralBenchRotZ, _cfgAstralBenchScale,
                _cfgAstralTablePosX, _cfgAstralTablePosY, _cfgAstralTablePosZ,
                _cfgAstralTableRotX, _cfgAstralTableRotY, _cfgAstralTableRotZ, _cfgAstralTableScale,
                _cfgAstralBedPosX, _cfgAstralBedPosY, _cfgAstralBedPosZ,
                _cfgAstralBedRotX, _cfgAstralBedRotY, _cfgAstralBedRotZ, _cfgAstralBedScale);

            // 形・色・本数が変わったら組み直す（遅延実行）
            Register(_sceneryRebuildEntries,
                _cfgAstralHillRadius, _cfgAstralHillPeak, _cfgAstralGroundTextureSize,
                _cfgAstralGroundExtentScale, _cfgAstralHorizonColor, _cfgAstralClearingRadius,
                _cfgAstralGroundFadeOut, _cfgAstralGroundFadeUnlit, _cfgAstralGroundUnlitBrightness,
                _cfgAstralGrassHeight, _cfgAstralGrassCount,
                _cfgAstralGrassBaseColor, _cfgAstralGrassTipColor,
                _cfgAstralGroundMossColor, _cfgAstralGroundTurfColor,
                _cfgAstralGroundDryColor, _cfgAstralGroundEarthColor,
                _cfgAstralWoodLightColor, _cfgAstralWoodDarkColor,
                _cfgAstralFabricColor, _cfgAstralBlanketColor,
                _cfgUseAstralBench, _cfgUseAstralTable, _cfgUseAstralBed,
                _cfgBedFollowOnInsertion);      // 追従先の刈り取り枠が変わるので組み直す

            // 個々の項目ではなく設定ファイル全体の変更通知を1本だけ受けて振り分ける。
            Config.SettingChanged += OnSceneryConfigChanged;
        }

        private static void Register(HashSet<ConfigEntryBase> set, params ConfigEntryBase[] entries)
        {
            for (int i = 0; i < entries.Length; i++)
                if (entries[i] != null) set.Add(entries[i]);
        }

        private void OnSceneryConfigChanged(object sender, SettingChangedEventArgs args)
        {
            if (args == null || args.ChangedSetting == null) return;

            // 軽いものから順に見る。ドラッグ中に重い処理へ落ちないことが最優先。
            if (_sceneryTransformEntries.Contains(args.ChangedSetting))
            {
                ApplyAstralHillTransform();
                return;
            }
            if (_furnitureTransformEntries.Contains(args.ChangedSetting))
            {
                ApplyFurnitureTransforms();
                RequestSceneryRebuild();     // 足元の草の刈り直しは遅れて追いつかせる
                return;
            }
            if (args.ChangedSetting == _cfgAstralSceneryFill)
            {
                ApplySceneryFill();
                return;
            }
            // 地平の色は霧の色でもある。焼き直しを待たずに霧だけ先に塗り替えて、
            // スライダーを動かしながら空と合わせられるようにする。
            if (args.ChangedSetting == _cfgAstralHorizonColor)
            {
                if (_astralHillRoot != null && _astralHillRoot.activeSelf) ApplyAstralFog();
                RequestSceneryRebuild();     // 地面の外周の焼き色は遅れて追いつかせる
                return;
            }
            if (args.ChangedSetting == _cfgUseAstralFog
                || args.ChangedSetting == _cfgAstralFogStart
                || args.ChangedSetting == _cfgAstralFogEnd)
            {
                if (_astralHillRoot != null && _astralHillRoot.activeSelf) ApplyAstralFog();
                else RestoreAstralFog("fog setting changed while hidden");
                return;
            }
            if (_sceneryRebuildEntries.Contains(args.ChangedSetting)) RequestSceneryRebuild();
        }

        /// <summary>
        /// 家具だけをその場で動かす。組み直さないので、スライダーを掴んだまま滑らかに動く。
        /// </summary>
        private void ApplyFurnitureTransforms()
        {
            if (_astralFurnitureByName.Count == 0) return;

            List<FurniturePlacement> placements = ReadFurniturePlacements();
            for (int i = 0; i < placements.Count; i++)
            {
                FurniturePlacement f = placements[i];
                GameObject go;
                if (!_astralFurnitureByName.TryGetValue(f.Name, out go) || go == null) continue;

                float groundY = HillHeight(f.LocalPosition.x, f.LocalPosition.z);
                go.transform.localPosition = new Vector3(f.LocalPosition.x,
                                                         groundY + f.LocalPosition.y,
                                                         f.LocalPosition.z);
                go.transform.localRotation = Quaternion.Euler(f.EulerAngles);
                go.transform.localScale = Vector3.one * f.Scale;
            }
        }

        /// <summary>景色の自己発光だけを塗り替える。組み直さないので明るさ調整は即座に効く。</summary>
        private void ApplySceneryFill()
        {
            float fill = _cfgAstralSceneryFill != null ? _cfgAstralSceneryFill.Value : 0.35f;
            for (int i = 0; i < _sceneryFillTargets.Count; i++)
            {
                SceneryFillTarget t = _sceneryFillTargets[i];
                ApplyMaterialFill(t.Material, t.BaseColour, t.Texture, fill);
            }
        }

        /// <summary>
        /// 形が変わる設定が動いたときの予約。ここでは組み直さない。
        /// スライダーは1目盛りごとに通知を投げてくるため、その場で組み直すと固まる。
        /// </summary>
        private void RequestSceneryRebuild()
        {
            _astralSceneryDirty = true;
            _sceneryRebuildAt = Time.unscaledTime + SceneryRebuildDelay;
        }

        /// <summary>
        /// 予約された組み直しを、手が止まってから一度だけ実行する。毎フレーム呼ぶ。
        /// </summary>
        internal void TickSceneryRebuild()
        {
            if (_sceneryRebuildAt < 0f) return;
            if (Time.unscaledTime < _sceneryRebuildAt) return;
            _sceneryRebuildAt = -1f;

            if (_astralHillRoot == null || !_astralHillRoot.activeSelf) return;   // 非表示中は次の表示時でよい
            DestroyAstralScenery();
            ShowAstralHill();
        }

        // ------------------------------------------------------------------
        // 表示・非表示
        // ------------------------------------------------------------------

        private void ShowAstralHill()
        {
            if (_cfgUseAstralHill == null || !_cfgUseAstralHill.Value)
            {
                HideAstralHill();
                SimpleFileLogger.Info("Astral scenery skipped by config");
                return;
            }

            if (_astralHillRoot == null || _astralSceneryDirty)
            {
                DestroyAstralScenery();
                if (!BuildAstralScenery())
                {
                    SimpleFileLogger.Warn("Astral scenery build failed");
                    return;
                }
            }

            ApplyAstralHillTransform();
            _astralHillRoot.SetActive(true);
            ApplyAstralFog();
            SimpleFileLogger.Info($"Astral scenery shown: root={DescribeGameObject(_astralHillRoot)}, "
                + $"pos={_astralHillRoot.transform.position}, furniture={_astralFurnitureRoots.Count}");
        }

        private void HideAstralHill()
        {
            RestoreAstralFog("scenery hidden");
            if (_astralHillRoot != null)
            {
                _astralHillRoot.SetActive(false);
                SimpleFileLogger.Info("Astral scenery hidden for reuse");
            }
        }

        /// <summary>
        /// 景色全体の置き場所。基準はアストラル側のキャラ中心。
        /// 丘の Transform 設定は丘と草にだけ効く。家具は自分の設定で独立して動く。
        /// </summary>
        private void ApplyAstralHillTransform()
        {
            if (_astralHillRoot == null) return;

            Vector3 basePos = GetAstralCharaCenter();
            if (basePos == Vector3.zero) basePos = GetAstralWorldOrigin();
            _astralHillRoot.transform.position = basePos;
            _astralHillRoot.transform.rotation = Quaternion.identity;
            _astralHillRoot.transform.localScale = Vector3.one;

            if (_astralHillGeometryRoot == null) return;

            _astralHillGeometryRoot.localPosition = new Vector3(
                _cfgAstralHillPosX != null ? _cfgAstralHillPosX.Value : 0f,
                _cfgAstralHillPosY != null ? _cfgAstralHillPosY.Value : 0f,
                _cfgAstralHillPosZ != null ? _cfgAstralHillPosZ.Value : 0f);
            _astralHillGeometryRoot.localRotation = Quaternion.Euler(
                _cfgAstralHillRotX != null ? _cfgAstralHillRotX.Value : 0f,
                _cfgAstralHillRotY != null ? _cfgAstralHillRotY.Value : 0f,
                _cfgAstralHillRotZ != null ? _cfgAstralHillRotZ.Value : 0f);
            _astralHillGeometryRoot.localScale = Vector3.one *
                Mathf.Max(0.01f, _cfgAstralHillScale != null ? _cfgAstralHillScale.Value : 1f);
        }

        // ------------------------------------------------------------------
        // 生成
        // ------------------------------------------------------------------

        private bool BuildAstralScenery()
        {
            Shader shader = ResolveSceneryShader();
            if (shader == null)
            {
                SimpleFileLogger.Warn("Astral scenery aborted: no usable shader (Standard / Unlit/Color both missing)");
                return false;
            }

            float radius = Mathf.Max(1f, _cfgAstralHillRadius != null ? _cfgAstralHillRadius.Value : 26f);
            // 地面は丘よりずっと遠くまで伸ばす。手前で切れると星空との境目が線になって見える。
            float extent = radius * Mathf.Max(1.6f, _cfgAstralGroundExtentScale != null ? _cfgAstralGroundExtentScale.Value : 3.2f);
            int textureSize = _cfgAstralGroundTextureSize != null ? _cfgAstralGroundTextureSize.Value : 1024;

            _astralHillRoot = new GameObject("AstralMode_Scenery");
            DontDestroyOnLoad(_astralHillRoot);
            _astralHillRoot.hideFlags = HideFlags.DontSave;

            GameObject geometry = new GameObject("Hill");
            geometry.transform.SetParent(_astralHillRoot.transform, false);
            geometry.hideFlags = HideFlags.DontSave;
            _astralHillGeometryRoot = geometry.transform;

            // --- 地面 ---
            // テクスチャは焼くのに一番時間がかかるので、中身を決める入力が同じなら使い回す。
            string groundKey = BuildGroundTextureKey(textureSize, extent);
            if (_astralGroundTexture == null || _astralGroundTextureKey != groundKey)
            {
                if (_astralGroundTexture != null) Destroy(_astralGroundTexture);
                _astralGroundTexture = BuildGroundTexture(textureSize, extent);
                _astralGroundTextureKey = groundKey;
                SimpleFileLogger.Info($"Astral ground texture baked: size={textureSize}, extent={extent:F1}");
            }
            Texture2D groundTex = _astralGroundTexture;

            bool fadeOut = _cfgAstralGroundFadeOut == null || _cfgAstralGroundFadeOut.Value;
            bool useUnlit = fadeOut && _cfgAstralGroundFadeUnlit != null && _cfgAstralGroundFadeUnlit.Value;

            Material groundMat = null;
            if (useUnlit)
            {
                groundMat = CreateUnlitFadeMaterial("AstralGround", groundTex,
                    _cfgAstralGroundUnlitBrightness != null ? _cfgAstralGroundUnlitBrightness.Value : 1f);
            }
            if (groundMat == null)
            {
                groundMat = CreateSceneryMaterial("AstralGround", Color.white, groundTex);
                if (fadeOut) ConfigureMaterialForFadeOut(groundMat);
                // 光を受ける塗りのときだけ自己発光の対象にする（発光しない塗りには効かない）
                RegisterFillTarget(groundMat, Color.white, groundTex);
            }
            _astralOwnedMaterials.Add(groundMat);
            SimpleFileLogger.Info($"Astral ground material: shader={groundMat.shader.name}, fadeOut={fadeOut}, unlit={useUnlit}");

            Mesh hillMesh = BuildHillMesh(extent, 180);
            _astralOwnedMeshes.Add(hillMesh);
            CreateMeshObject("Ground", geometry.transform, hillMesh, groundMat);

            // --- 家具の据え付けを先に決める。草を刈る範囲がこれで決まるため ---
            List<FurniturePlacement> placements = ReadFurniturePlacements();

            // --- 草 ---
            Texture2D grassTex = BuildGrassGradientTexture(64);
            _astralOwnedTextures.Add(grassTex);
            Material grassMat = CreateSceneryMaterial("AstralGrass", Color.white, grassTex);
            _astralOwnedMaterials.Add(grassMat);
            RegisterFillTarget(grassMat, Color.white, grassTex);

            int grassCount = Mathf.Max(0, _cfgAstralGrassCount != null ? _cfgAstralGrassCount.Value : 22000);
            float grassScale = Mathf.Max(0.01f, _cfgAstralGrassHeight != null ? _cfgAstralGrassHeight.Value : 1f);
            float grassRadius = radius * 0.78f;

            System.Random rng = new System.Random(GrassSeed);

            // キャラは常にアストラルの基準点に居る。丘はその基準点から Position だけずらして
            // 置いてあるので、丘ローカルで見たキャラの位置は Position の符号を反転したもの。
            Vector2 clearCentre = new Vector2(
                -(_cfgAstralHillPosX != null ? _cfgAstralHillPosX.Value : 0f),
                -(_cfgAstralHillPosZ != null ? _cfgAstralHillPosZ.Value : 0f));
            float clearRadius = _cfgAstralClearingRadius != null ? _cfgAstralClearingRadius.Value : 2.2f;

            int mainBlades = ScatterGrass(geometry.transform, grassMat, rng, placements,
                grassCount, grassRadius, grassScale,
                0.22f, 0.55f, 0.045f, 0.25f, 0.75f, 0.25f, 1.0f, 0.045f, 4, "Grass",
                clearCentre, clearRadius);

            // 下草。地面と草の境目を埋める。これが無いと根元で地面がスパッと見えて浮く。
            int underBlades = ScatterGrass(geometry.transform, grassMat, rng, placements,
                Mathf.RoundToInt(grassCount * 1.2f), grassRadius, grassScale,
                0.05f, 0.15f, 0.12f, 0.90f, 2.00f, 0.0f, 0.35f, 0.075f, 2, "Undergrowth",
                clearCentre, clearRadius * 0.72f);   // 下草は中心近くまで残して地面の裸を隠す

            // --- 家具 ---
            Color woodLight = _cfgAstralWoodLightColor != null ? _cfgAstralWoodLightColor.Value : new Color(0.596f, 0.502f, 0.376f);
            Color woodDark = _cfgAstralWoodDarkColor != null ? _cfgAstralWoodDarkColor.Value : new Color(0.408f, 0.329f, 0.243f);
            Color fabric = _cfgAstralFabricColor != null ? _cfgAstralFabricColor.Value : new Color(0.808f, 0.792f, 0.753f);
            Color blanket = _cfgAstralBlanketColor != null ? _cfgAstralBlanketColor.Value : new Color(0.518f, 0.573f, 0.627f);

            Material[] furnitureMaterials = new Material[5];
            furnitureMaterials[MatWoodLight] = CreateFurnitureMaterial("AstralWoodLight", woodLight, true);
            furnitureMaterials[MatWoodDark] = CreateFurnitureMaterial("AstralWoodDark", woodDark, true);
            furnitureMaterials[MatFabric] = CreateFurnitureMaterial("AstralFabric", fabric, false);
            furnitureMaterials[MatPillow] = CreateFurnitureMaterial("AstralPillow", new Color(0.925f, 0.918f, 0.894f), false);
            furnitureMaterials[MatBlanket] = CreateFurnitureMaterial("AstralBlanket", blanket, false);

            // 家具は丘と同じ親にぶら下げる。丘を動かせば家具も一緒に動き、
            // 草を刈る範囲の計算（丘ローカル座標）とも食い違わない。
            BuildFurniture(geometry.transform, placements, furnitureMaterials);

            ApplySceneryFill();

            _astralSceneryDirty = false;
            SimpleFileLogger.Info($"Astral scenery built: shader={shader.name}, radius={radius:F1}, extent={extent:F1}, "
                + $"groundTex={textureSize}, blades={mainBlades}+{underBlades}, furniture={_astralFurnitureRoots.Count}, "
                + $"fill={(_cfgAstralSceneryFill != null ? _cfgAstralSceneryFill.Value : 0.35f):F2}");
            return true;
        }

        /// <summary>家具のマテリアルを1枚作って、破棄対象と明るさ調整対象に登録するところまでやる。</summary>
        private Material CreateFurnitureMaterial(string name, Color colour, bool woodGrain)
        {
            Texture2D tex = BuildSurfaceTexture(name, colour, woodGrain, 64);
            _astralOwnedTextures.Add(tex);

            Material mat = CreateSceneryMaterial(name, Color.white, tex);
            _astralOwnedMaterials.Add(mat);
            RegisterFillTarget(mat, colour, tex);
            return mat;
        }

        private void RegisterFillTarget(Material mat, Color baseColour, Texture2D texture)
        {
            SceneryFillTarget t;
            t.Material = mat;
            t.BaseColour = baseColour;
            t.Texture = texture;
            _sceneryFillTargets.Add(t);
        }

        /// <summary>地面テクスチャの中身を決める入力だけを並べた鍵。同じなら焼き直さない。</summary>
        private string BuildGroundTextureKey(int size, float extent)
        {
            return size.ToString() + "|" + extent.ToString("F3")
                + "|" + (_cfgAstralHillRadius != null ? _cfgAstralHillRadius.Value : 26f).ToString("F3")
                + "|" + (_cfgAstralHillPeak != null ? _cfgAstralHillPeak.Value : 5f).ToString("F3")
                + "|" + ColourKey(_cfgAstralGroundMossColor)
                + "|" + ColourKey(_cfgAstralGroundTurfColor)
                + "|" + ColourKey(_cfgAstralGroundDryColor)
                + "|" + ColourKey(_cfgAstralGroundEarthColor)
                + "|" + ColourKey(_cfgAstralHorizonColor);
        }

        private static string ColourKey(ConfigEntry<Color> entry)
        {
            if (entry == null) return "-";
            Color c = entry.Value;
            return c.r.ToString("F3") + "," + c.g.ToString("F3") + "," + c.b.ToString("F3");
        }

        /// <summary>
        /// 草を撒いて必要なだけメッシュに分けて置く。
        /// 株でまとまって生やし、裾と枯れ斑の上は間引き、家具の足元は刈る。
        /// </summary>
        private int ScatterGrass(Transform parent, Material material, System.Random rng,
                                 List<FurniturePlacement> placements,
                                 int count, float areaRadius, float scale,
                                 float hMin, float hMax, float spread,
                                 float leanMin, float leanMax,
                                 float toneMin, float toneMax, float widthRatio,
                                 int segments, string label,
                                 Vector2 clearCentre, float clearRadius)
        {
            if (count <= 0) return 0;

            int clumpCount = Mathf.Max(1, count / 34);
            Vector2[] clumps = new Vector2[clumpCount];
            for (int i = 0; i < clumpCount; i++)
            {
                double a = rng.NextDouble() * Math.PI * 2.0;
                double r = areaRadius * Math.Sqrt(rng.NextDouble());
                clumps[i] = new Vector2((float)(Math.Cos(a) * r), (float)(Math.Sin(a) * r));
            }

            List<Mesh> chunks = new List<Mesh>();
            MeshBuilder mb = new MeshBuilder();
            int placed = 0;

            for (int i = 0; i < count; i++)
            {
                Vector2 clump = clumps[rng.Next(clumpCount)];
                float x = clump.x + Gaussian(rng) * areaRadius * spread;
                float z = clump.y + Gaussian(rng) * areaRadius * spread;

                // 裾へ行くほど間引く。円形に断ち切ると人工的な縁が出る。
                float edge = Mathf.Sqrt(x * x + z * z) / areaRadius;
                if (edge > 0.72f && rng.NextDouble() < (edge - 0.72f) / 0.42f) continue;

                // 地面の枯れ斑の上は薄くする。模様と生え方を連動させると
                // 「塗り絵の上に草を置いた」感じが消える。
                float thin = Fbm(x * 0.42f, z * 0.42f, 54.17f, 3);
                if (thin > 0.60f && rng.NextDouble() < (thin - 0.60f) / 0.34f) continue;

                if (IsUnderFurniture(placements, x, z)) continue;

                // キャラの周りは草を空ける。H中に草が視界を邪魔しないため。
                // 円形にスパッと刈ると禿げた輪ができるので、中心だけ完全に空け、
                // 外へ向かって密度と丈を戻す（踏み倒された跡のように見せる）。
                float clearScale = 1f;
                if (clearRadius > 0.01f)
                {
                    float cdx = x - clearCentre.x;
                    float cdz = z - clearCentre.y;
                    float cdist = Mathf.Sqrt(cdx * cdx + cdz * cdz);
                    float core = clearRadius * 0.55f;
                    if (cdist < core) continue;
                    if (cdist < clearRadius)
                    {
                        float k = (cdist - core) / Mathf.Max(0.001f, clearRadius - core);
                        if (rng.NextDouble() > k) continue;          // 密度を戻していく
                        clearScale = Mathf.Lerp(0.28f, 1f, k);       // 丈も戻していく
                    }
                }

                float h = scale * clearScale * Mathf.Lerp(hMin, hMax, (float)rng.NextDouble());
                float w = h * Mathf.Lerp(widthRatio, widthRatio * 1.7f, (float)rng.NextDouble());
                float ang = (float)(rng.NextDouble() * Math.PI * 2.0);
                Vector3 bend = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                float lean = h * Mathf.Lerp(leanMin, leanMax, (float)rng.NextDouble());
                float droop = Mathf.Lerp(0.58f, 0.90f, (float)rng.NextDouble());
                float tone = Mathf.Lerp(toneMin, toneMax, (float)rng.NextDouble());

                AppendBlade(mb, new Vector3(x, HillHeight(x, z), z), bend, h, w, lean, droop, tone, segments);
                placed++;

                // 1メッシュが大きくなりすぎる前に切る。描画のカリングも効きやすくなる。
                if (mb.VertexCount > 180000)
                {
                    chunks.Add(mb.ToMesh(label + "_" + chunks.Count));
                    mb = new MeshBuilder();
                }
            }

            if (mb.VertexCount > 0) chunks.Add(mb.ToMesh(label + "_" + chunks.Count));

            for (int i = 0; i < chunks.Count; i++)
            {
                _astralOwnedMeshes.Add(chunks[i]);
                CreateMeshObject(label + "_" + i, parent, chunks[i], material);
            }

            SimpleFileLogger.Info($"Astral {label}: requested={count}, placed={placed}, chunks={chunks.Count}");
            return placed;
        }

        /// <summary>正規分布の乱数。株のばらけ方に使う（一様だと格子状に見える）。</summary>
        private static float Gaussian(System.Random rng)
        {
            double u1 = 1.0 - rng.NextDouble();
            double u2 = rng.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }

        // ------------------------------------------------------------------
        // 破棄
        // ------------------------------------------------------------------

        /// <summary>親を消せば必ず元通り、を守るための後始末。</summary>
        private void DestroyAstralScenery()
        {
            if (_astralHillRoot != null)
            {
                Destroy(_astralHillRoot);
                _astralHillRoot = null;
            }
            _astralHillGeometryRoot = null;
            _astralFurnitureRoots.Clear();
            _astralFurnitureByName.Clear();
            _sceneryFillTargets.Clear();

            for (int i = 0; i < _astralOwnedMeshes.Count; i++)
                if (_astralOwnedMeshes[i] != null) Destroy(_astralOwnedMeshes[i]);
            _astralOwnedMeshes.Clear();

            for (int i = 0; i < _astralOwnedMaterials.Count; i++)
                if (_astralOwnedMaterials[i] != null) Destroy(_astralOwnedMaterials[i]);
            _astralOwnedMaterials.Clear();

            for (int i = 0; i < _astralOwnedTextures.Count; i++)
                if (_astralOwnedTextures[i] != null) Destroy(_astralOwnedTextures[i]);
            _astralOwnedTextures.Clear();
        }

        private void UnloadAstralHill()
        {
            RestoreAstralFog("scenery unloaded");
            DestroyAstralScenery();
            if (_astralGroundTexture != null)
            {
                Destroy(_astralGroundTexture);
                _astralGroundTexture = null;
                _astralGroundTextureKey = null;
            }
            _astralSceneryDirty = true;
            SimpleFileLogger.Info("Astral scenery unloaded");
        }

        private static string DescribeGameObject(GameObject go)
        {
            if (go == null) return "null";
            return $"name={go.name}, active={go.activeSelf}, instId={go.GetInstanceID()}";
        }

        private static string DescribeGameObjects(GameObject[] objects)
        {
            if (objects == null) return "null";
            if (objects.Length == 0) return "empty";
            string s = string.Empty;
            int max = Math.Min(objects.Length, 20);
            for (int i = 0; i < max; i++)
            {
                if (i > 0) s += " | ";
                s += DescribeGameObject(objects[i]);
            }
            if (objects.Length > max) s += " | ...";
            return s;
        }
    }
}
