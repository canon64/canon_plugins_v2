using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AstralMode
{
    /// <summary>
    /// アストラル界の景色をランタイムで組み立てるための土台。
    ///
    /// 外部 AssetBundle を一切使わない。丘の形も、地面の模様も、草の葉の形も、
    /// すべてここで数式から生成する。外から持ち込んだマテリアルが実機で剥がれて
    /// 「丘が茶色」「草が三角ポリゴン」になった事故（IMPL_PLAN_hill_rebuild.md）の再発を
    /// 構造的に不可能にするのが目的。
    ///
    /// 使う筆は Standard 1本だけ。白い部屋（Plugin.AstralWorld.cs）で実機成立済みの筆であり、
    /// アルファ抜きも独自シェーダーも使わないため、抜きが効かず板が三角に見える事故が起きない。
    /// </summary>
    public sealed partial class Plugin
    {
        // ------------------------------------------------------------------
        // 揺らぎ（Perlin を重ねる）
        // ------------------------------------------------------------------

        /// <summary>Perlin を数段重ねた揺らぎ。0..1。地面の模様と草の疎密の両方に使う。</summary>
        private static float Fbm(float x, float z, float seed, int octaves)
        {
            float total = 0f;
            float amp = 1f;
            float freq = 1f;
            float norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                total += Mathf.PerlinNoise(x * freq + seed + i * 17.13f,
                                           z * freq + seed - i * 9.71f) * amp;
                norm += amp;
                amp *= 0.5f;
                freq *= 2f;
            }
            return norm > 0f ? total / norm : 0f;
        }

        // ------------------------------------------------------------------
        // 丘の高さ
        // ------------------------------------------------------------------

        /// <summary>
        /// 丘のローカル原点からの高さ。中央の大きな盛り上がりに、大小2段のうねりを重ねる。
        /// 裾へ向かうほどうねりを消して平らに落とし、断ち切った縁が出ないようにする。
        /// </summary>
        private float HillHeight(float x, float z)
        {
            float radius = Mathf.Max(1f, _cfgAstralHillRadius != null ? _cfgAstralHillRadius.Value : 26f);
            float peak = _cfgAstralHillPeak != null ? _cfgAstralHillPeak.Value : 5f;

            float r = Mathf.Sqrt(x * x + z * z) / radius;
            float main = peak * Mathf.Exp(-(r * r) * 1.6f);

            float k = peak / 5f;                                  // うねりの大きさは丘の高さに追従させる
            float swell = 0.55f * k * Mathf.Sin(x * 0.20f + 1.3f) * Mathf.Cos(z * 0.17f - 0.6f);
            float ripple = 0.18f * k * Mathf.Sin(x * 0.61f - 0.4f) * Mathf.Sin(z * 0.55f + 2.1f);
            float fade = Mathf.Clamp01(1f - r);

            return main + (swell + ripple) * fade;
        }

        private Vector3 HillNormal(float x, float z)
        {
            const float eps = 0.35f;
            float hx = HillHeight(x + eps, z) - HillHeight(x - eps, z);
            float hz = HillHeight(x, z + eps) - HillHeight(x, z - eps);
            return new Vector3(-hx, 2f * eps, -hz).normalized;
        }

        // ------------------------------------------------------------------
        // マテリアル
        // ------------------------------------------------------------------

        private static Shader ResolveSceneryShader()
        {
            // Standard は白い部屋で実機成立済み。Unlit/Color は最後の保険。
            Shader shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            return shader;
        }

        private static Material CreateSceneryMaterial(string name, Color colour, Texture2D texture)
        {
            Shader shader = ResolveSceneryShader();
            Material mat = new Material(shader);
            mat.name = name;
            mat.color = colour;
            if (texture != null) mat.mainTexture = texture;
            // 草も土も木も、光沢は要らない。反射が乗ると夜の景色が安っぽくなる。
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.08f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            mat.hideFlags = HideFlags.DontSave;
            return mat;
        }

        /// <summary>
        /// 景色が自分の色で薄く光るようにする。
        ///
        /// アストラル界の光源はキャラ頭上の点光源1個だけで、マップ照明は BlankMapAdd が落としている。
        /// 環境光をあてにする塗り方だと、光が直接当たらない場所が真っ黒に沈む。
        /// かといって RenderSettings の環境光を上げると現実に漏れるので触れない
        /// （Plugin.AstralLight.cs の決めごと）。
        /// そこで「景色自身がほのかに自分の色を発する」ことで底上げする。
        /// これはマテリアル単位の設定なので、この景色を消せば必ず元通りになる。
        /// </summary>
        private static void ApplyMaterialFill(Material mat, Color colour, Texture2D texture, float fill)
        {
            if (mat == null || !mat.HasProperty("_EmissionColor")) return;

            fill = Mathf.Clamp01(fill);
            if (fill <= 0.001f)
            {
                mat.DisableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.black);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                return;
            }

            mat.EnableKeyword("_EMISSION");
            if (texture != null && mat.HasProperty("_EmissionMap"))
            {
                // 模様つきの面は、模様どおりに光らせる（べた塗りで光らせると模様が消える）
                mat.SetTexture("_EmissionMap", texture);
                mat.SetColor("_EmissionColor", new Color(fill, fill, fill, 1f));
            }
            else
            {
                mat.SetColor("_EmissionColor", colour * fill);
            }
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        // ------------------------------------------------------------------
        // 地面のテクスチャ
        // ------------------------------------------------------------------

        /// <summary>
        /// 地面の模様を1枚の絵として焼く。
        /// 面ごとに色を置くと必ず升目が見えるため、模様はテクスチャで与える（プレビューで実証済み）。
        /// 苔の濃緑・草地・枯れ・土 の4色を揺らぎで混ぜ、最後に細かい粒を重ねる。
        /// </summary>
        private Texture2D BuildGroundTexture(int size, float worldExtent)
        {
            Color moss = _cfgAstralGroundMossColor != null ? _cfgAstralGroundMossColor.Value : new Color(0.086f, 0.180f, 0.086f);
            Color turf = _cfgAstralGroundTurfColor != null ? _cfgAstralGroundTurfColor.Value : new Color(0.212f, 0.361f, 0.165f);
            Color dry = _cfgAstralGroundDryColor != null ? _cfgAstralGroundDryColor.Value : new Color(0.408f, 0.424f, 0.220f);
            Color earth = _cfgAstralGroundEarthColor != null ? _cfgAstralGroundEarthColor.Value : new Color(0.298f, 0.243f, 0.173f);

            // 外周は夜の闇へ沈める。地面が途中でスパッと終わって星空との境目が線に見えるのを消すため。
            // 霧を使えば早いが、霧はゲーム全体の設定なので現実に漏れる。だから絵の側で沈める。
            Color horizon = _cfgAstralHorizonColor != null ? _cfgAstralHorizonColor.Value : new Color(0.047f, 0.063f, 0.133f);
            float radius = Mathf.Max(1f, _cfgAstralHillRadius != null ? _cfgAstralHillRadius.Value : 26f);
            float fadeStart = radius * 0.86f;      // 草地が尽きるあたりから沈み始める
            float fadeEnd = worldExtent;

            // 外周を透明へ抜くので RGBA が要る。透明にすれば背後の空がそのまま出るため、
            // 空の色を当てにいかなくても境目が必ず消える。
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.name = "AstralGround";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.hideFlags = HideFlags.DontSave;

            Color[] pixels = new Color[size * size];
            float span = worldExtent * 2f;

            for (int py = 0; py < size; py++)
            {
                float wz = (py / (float)(size - 1)) * span - worldExtent;
                for (int px = 0; px < size; px++)
                {
                    float wx = (px / (float)(size - 1)) * span - worldExtent;

                    float broad = Fbm(wx * 0.075f, wz * 0.075f, 13.01f, 4);      // 広い色ムラ
                    float patch = Fbm(wx * 0.42f, wz * 0.42f, 54.17f, 3);        // 枯れ斑
                    float bare = Fbm(wx * 0.30f + 11f, wz * 0.30f - 7f, 90.91f, 3); // 地肌
                    float grain = Fbm(wx * 3.10f, wz * 3.10f, 27.49f, 2);        // 細かい粒

                    Color c = Color.Lerp(moss, turf, Mathf.Clamp01(broad * 1.35f));
                    c = Color.Lerp(c, dry, Mathf.Clamp01((patch - 0.56f) / 0.30f) * 0.75f);
                    c = Color.Lerp(c, earth, Mathf.Clamp01((bare - 0.70f) / 0.22f) * 0.80f);

                    // 窪みは湿って暗く、盛り上がりは乾いて明るく。地形と模様を連動させる。
                    float slope = Mathf.Clamp01(HillHeight(wx, wz) / Mathf.Max(0.001f, _cfgAstralHillPeak != null ? _cfgAstralHillPeak.Value : 5f) * 0.5f + 0.5f);
                    float mul = (0.86f + 0.20f * slope) * (0.84f + 0.32f * grain);
                    c = new Color(c.r * mul, c.g * mul, c.b * mul, 1f);

                    // 中心から離れるほど夜へ沈め、さらに透明へ抜く。
                    // 境目が真円の線にならないよう、距離そのものを揺らぎで崩す。
                    float dist = Mathf.Sqrt(wx * wx + wz * wz);
                    dist += (Fbm(wx * 0.045f, wz * 0.045f, 71.3f, 3) - 0.5f) * (fadeEnd - fadeStart) * 0.22f;
                    float t = Mathf.Clamp01((dist - fadeStart) / Mathf.Max(0.001f, fadeEnd - fadeStart));
                    t = t * t * (3f - 2f * t);          // なめらかに
                    c = Color.Lerp(c, horizon, t);
                    c.a = 1f - t;                        // 外へ行くほど消えて空が透ける

                    pixels[py * size + px] = c;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(true);
            return tex;
        }

        /// <summary>
        /// 草の色見本。横軸＝株ごとの色味、縦軸＝根元から先端へのグラデーション。
        /// 1本ごとの色ムラを、マテリアルを増やさずに UV だけで表現するための仕掛け。
        /// </summary>
        private Texture2D BuildGrassGradientTexture(int size)
        {
            Color baseCol = _cfgAstralGrassBaseColor != null ? _cfgAstralGrassBaseColor.Value : new Color(0.102f, 0.227f, 0.094f);
            Color tipCol = _cfgAstralGrassTipColor != null ? _cfgAstralGrassTipColor.Value : new Color(0.518f, 0.769f, 0.306f);

            Texture2D tex = new Texture2D(size, size, TextureFormat.RGB24, true);
            tex.name = "AstralGrassGradient";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.hideFlags = HideFlags.DontSave;

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float t = Mathf.Pow(y / (float)(size - 1), 0.8f);        // 根元→先端
                Color grad = Color.Lerp(baseCol, tipCol, t);
                for (int x = 0; x < size; x++)
                {
                    float tone = 0.72f + 0.52f * (x / (float)(size - 1)); // 株ごとの明暗
                    pixels[y * size + x] = new Color(grad.r * tone, grad.g * tone, grad.b * tone, 1f);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply(true);
            return tex;
        }

        /// <summary>
        /// 家具用の小さなテクスチャを焼く。木目・織りのムラを与えるためだけのもの。
        /// べた塗りのままだと板がプラスチックの塊に見えるので、浅いムラを乗せる。
        /// </summary>
        private static Texture2D BuildSurfaceTexture(string name, Color colour, bool woodGrain, int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGB24, true);
            tex.name = name;
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.hideFlags = HideFlags.DontSave;

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size;
                    float v = y / (float)size;

                    float mul;
                    if (woodGrain)
                    {
                        // 木目：一方向に伸ばした縞に細かい揺らぎを混ぜる
                        float streak = Mathf.PerlinNoise(u * 3.5f, v * 26f);
                        float fibre = Mathf.PerlinNoise(u * 9f + 31f, v * 90f + 17f);
                        mul = 0.84f + 0.22f * streak + 0.08f * fibre;
                    }
                    else
                    {
                        // 布：ごく浅い織りムラだけ
                        float weave = Mathf.PerlinNoise(u * 40f, v * 40f);
                        mul = 0.93f + 0.12f * weave;
                    }

                    pixels[y * size + x] = new Color(colour.r * mul, colour.g * mul, colour.b * mul, 1f);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply(true);
            return tex;
        }

        // ------------------------------------------------------------------
        // メッシュ組み立ての受け皿
        // ------------------------------------------------------------------

        /// <summary>
        /// 頂点を溜めてメッシュに焼くための受け皿。
        /// 65535 頂点の壁は 32bit インデックスで越える（Unity 2019.4 で使える）。
        /// </summary>
        private sealed class MeshBuilder
        {
            internal readonly List<Vector3> Vertices = new List<Vector3>();
            internal readonly List<Vector3> Normals = new List<Vector3>();
            internal readonly List<Vector2> Uvs = new List<Vector2>();
            internal readonly List<int> Triangles = new List<int>();

            internal int VertexCount { get { return Vertices.Count; } }

            internal void AddTriangle(int a, int b, int c)
            {
                Triangles.Add(a);
                Triangles.Add(b);
                Triangles.Add(c);
            }

            internal Mesh ToMesh(string name)
            {
                Mesh mesh = new Mesh();
                mesh.name = name;
                mesh.indexFormat = Vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.SetVertices(Vertices);
                mesh.SetNormals(Normals);
                mesh.SetUVs(0, Uvs);
                mesh.SetTriangles(Triangles, 0, true);
                mesh.RecalculateBounds();
                mesh.hideFlags = HideFlags.DontSave;
                return mesh;
            }
        }

        // ------------------------------------------------------------------
        // 丘のメッシュ
        // ------------------------------------------------------------------

        private Mesh BuildHillMesh(float extent, int divisions)
        {
            MeshBuilder mb = new MeshBuilder();
            float step = (extent * 2f) / divisions;

            for (int iz = 0; iz <= divisions; iz++)
            {
                float z = -extent + step * iz;
                for (int ix = 0; ix <= divisions; ix++)
                {
                    float x = -extent + step * ix;
                    mb.Vertices.Add(new Vector3(x, HillHeight(x, z), z));
                    mb.Normals.Add(HillNormal(x, z));
                    mb.Uvs.Add(new Vector2(ix / (float)divisions, iz / (float)divisions));
                }
            }

            int row = divisions + 1;
            for (int iz = 0; iz < divisions; iz++)
            {
                for (int ix = 0; ix < divisions; ix++)
                {
                    int a = iz * row + ix;
                    int b = a + 1;
                    int c = a + row;
                    int d = c + 1;
                    mb.AddTriangle(a, c, d);
                    mb.AddTriangle(a, d, b);
                }
            }

            return mb.ToMesh("AstralHill");
        }

        /// <summary>
        /// 地面を「透ける塗り」に切り替える。
        ///
        /// 地面は1枚のまま、絵に焼いた薄さがそのまま濃さになる。材質は場所によらず同じなので、
        /// 途中で切り替わる段差が原理的に出ない。中心は完全な不透明、外周へ向かって消えていく。
        ///
        /// 半透明にしても奥行きの判定は生きているので、手前の草がリングに塗り潰されることはない。
        /// </summary>
        private static void ConfigureMaterialForFadeOut(Material mat)
        {
            if (mat == null) return;

            // Standard を Fade（そのまま薄くなる）へ切り替える手順一式。
            // どれか一つ欠けても効かないので、まとめて設定する。
            if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode", 2f);
            if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            // 奥行きの書き込みは残す。広い地面を寝かせて見ると、奥の面が手前の面より後に
            // 描かれて重なりが乱れる。書き込みを残せば手前が必ず勝つ。
            // 空は不透明の後・半透明の前に描かれるので、この地面は空の上に重なる。順序は問題ない。
            if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 1);

            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
        }

        /// <summary>
        /// 薄める機能が確実に生きている筆で地面を塗る、という逃げ道。
        ///
        /// 借り物の筆（Standard）は、ゲームの出荷時に使わない機能を削られていることがある。
        /// 霧がまさにそれで、設定は届くのに一切描かれなかった。薄める機能も同じ目に遭う可能性がある。
        /// ここで使う筆は、同じゲームで動いている他のプラグインが実際に半透明を出せている筆なので、
        /// 薄まらないという事故が起きない。代わりに光を受けないので、地面は一様な明るさになる。
        /// </summary>
        private static Material CreateUnlitFadeMaterial(string name, Texture2D texture, float brightness)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null) shader = Shader.Find("Particles/Alpha Blended");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (shader == null)
            {
                SimpleFileLogger.Warn("Astral ground unlit-fade: no transparent shader found");
                return null;
            }

            Material mat = new Material(shader);
            mat.name = name;
            if (texture != null) mat.mainTexture = texture;
            float b = Mathf.Max(0f, brightness);
            if (mat.HasProperty("_Color")) mat.color = new Color(b, b, b, 1f);
            if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 1);
            mat.renderQueue = 3000;
            mat.hideFlags = HideFlags.DontSave;
            SimpleFileLogger.Info($"Astral ground unlit-fade material: shader={shader.name}, brightness={b:F2}");
            return mat;
        }

        // ------------------------------------------------------------------
        // 草1本
        // ------------------------------------------------------------------

        /// <summary>
        /// 根元が細く出て途中で最も太く、先端が一点に尖って収束する葉を1枚積む。
        /// 中心線は二次ベジェで、立ち上がってから自重で流れる弧を描く。
        /// 裏面も張るので、横から見ても消えない。
        /// アルファ抜きを使わないため、抜きが壊れて三角に見える事故が起きない。
        /// </summary>
        private static void AppendBlade(MeshBuilder mb, Vector3 basePos, Vector3 bendDir,
                                        float height, float width, float lean, float droop,
                                        float tone, int segments)
        {
            Vector3 side = new Vector3(-bendDir.z, 0f, bendDir.x);
            Vector3 p0 = Vector3.zero;
            Vector3 p1 = Vector3.up * (height * 0.72f);
            Vector3 p2 = bendDir * lean + Vector3.up * (height * droop);

            int start = mb.VertexCount;

            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float mt = 1f - t;
                Vector3 centre = mt * mt * p0 + 2f * mt * t * p1 + t * t * p2;

                // 根元だけ細く絞る。(1-t)^n だけだと根元が角材のように見える。
                float w = i == segments
                    ? 0f
                    : width * Mathf.Pow(mt, 0.55f) * Mathf.Min(1f, 0.32f + t * 9f);

                Vector3 c = basePos + centre;
                mb.Vertices.Add(c - side * (w * 0.5f));
                mb.Vertices.Add(c + side * (w * 0.5f));
                mb.Uvs.Add(new Vector2(tone, t));
                mb.Uvs.Add(new Vector2(tone, t));
            }

            // 法線は葉の面から求める。1枚の葉の中では緩やかにしか変わらないので代表値で足りる。
            Vector3 faceNormal = Vector3.Cross(side, (p2 - p0).normalized).normalized;
            if (faceNormal.y < 0f) faceNormal = -faceNormal;
            faceNormal = Vector3.Slerp(faceNormal, Vector3.up, 0.45f).normalized;
            for (int i = 0; i <= segments; i++)
            {
                mb.Normals.Add(faceNormal);
                mb.Normals.Add(faceNormal);
            }

            for (int i = 0; i < segments; i++)
            {
                int a = start + i * 2;
                int b = a + 1;
                int c = a + 2;
                int d = a + 3;
                mb.AddTriangle(a, c, d);
                mb.AddTriangle(a, d, b);
                mb.AddTriangle(a, d, c);   // 裏面
                mb.AddTriangle(a, b, d);
            }
        }

        // ------------------------------------------------------------------
        // 直方体（家具の部材）
        // ------------------------------------------------------------------

        private static void AppendBox(MeshBuilder mb, Vector3 centre, Vector3 size)
        {
            Vector3 h = size * 0.5f;

            // 面ごとに頂点を分けて、角がぼやけないようにする。
            AppendQuad(mb, centre + new Vector3(-h.x, h.y, -h.z), centre + new Vector3(h.x, h.y, -h.z),
                           centre + new Vector3(h.x, h.y, h.z), centre + new Vector3(-h.x, h.y, h.z), Vector3.up);
            AppendQuad(mb, centre + new Vector3(-h.x, -h.y, h.z), centre + new Vector3(h.x, -h.y, h.z),
                           centre + new Vector3(h.x, -h.y, -h.z), centre + new Vector3(-h.x, -h.y, -h.z), Vector3.down);
            AppendQuad(mb, centre + new Vector3(-h.x, -h.y, -h.z), centre + new Vector3(h.x, -h.y, -h.z),
                           centre + new Vector3(h.x, h.y, -h.z), centre + new Vector3(-h.x, h.y, -h.z), Vector3.back);
            AppendQuad(mb, centre + new Vector3(h.x, -h.y, h.z), centre + new Vector3(-h.x, -h.y, h.z),
                           centre + new Vector3(-h.x, h.y, h.z), centre + new Vector3(h.x, h.y, h.z), Vector3.forward);
            AppendQuad(mb, centre + new Vector3(h.x, -h.y, -h.z), centre + new Vector3(h.x, -h.y, h.z),
                           centre + new Vector3(h.x, h.y, h.z), centre + new Vector3(h.x, h.y, -h.z), Vector3.right);
            AppendQuad(mb, centre + new Vector3(-h.x, -h.y, h.z), centre + new Vector3(-h.x, -h.y, -h.z),
                           centre + new Vector3(-h.x, h.y, -h.z), centre + new Vector3(-h.x, h.y, h.z), Vector3.left);
        }

        private static void AppendQuad(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            int start = mb.VertexCount;
            mb.Vertices.Add(a); mb.Vertices.Add(b); mb.Vertices.Add(c); mb.Vertices.Add(d);
            for (int i = 0; i < 4; i++) mb.Normals.Add(normal);
            mb.Uvs.Add(new Vector2(0f, 0f));
            mb.Uvs.Add(new Vector2(1f, 0f));
            mb.Uvs.Add(new Vector2(1f, 1f));
            mb.Uvs.Add(new Vector2(0f, 1f));
            // 頂点は a→b→c→d と外から見て反時計回りに並べてある。
            // Unity は「表から見て時計回り」を表とするので、そのまま繋ぐと全部の面が裏返り、
            // 手前の面が消えて箱の内側が見える（＝トレイのように見える／透けて見える）。
            // ここで順を逆に繋いで、宣言した法線と面の向きを一致させる。
            mb.AddTriangle(start, start + 2, start + 1);
            mb.AddTriangle(start, start + 3, start + 2);
        }

        // ------------------------------------------------------------------
        // 生成物を1つの親にぶら下げるための道具
        // ------------------------------------------------------------------

        private static GameObject CreateMeshObject(string name, Transform parent, Mesh mesh, Material material)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.hideFlags = HideFlags.DontSave;

            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;   // 光源は点光源1個。影を落とすと重いだけで得がない
            mr.receiveShadows = false;

            return go;
        }
    }
}
