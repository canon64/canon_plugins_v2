using UnityEngine;

namespace AstralMode
{
    // メインカメラに付与するラジアルブラー画像エフェクト。
    // Camera.OnRenderImage で Graphics.Blit にカスタムマテリアルを通す。
    // _Strength=0 のときは Blit のみ (実質パススルー)
    // ランタイム生成シェーダー方式 (BepInExでよく使われる手法)
    internal sealed class RadialBlurEffect : MonoBehaviour
    {
        public Vector2 Center = new Vector2(0.5f, 0.5f);
        public float Strength = 0f;

        private Material _mat;
        private static Shader _cachedShader;

        // Unity 内蔵 image-effect 風シェーダーをランタイム生成
        // 8サンプル固定 (シェーダーモデル互換性のため)
        private const string ShaderText = @"
Shader ""Hidden/AstralRadialBlur""
{
    Properties
    {
        _MainTex (""Source"", 2D) = ""white"" {}
        _Center (""Center"", Vector) = (0.5, 0.5, 0, 0)
        _Strength (""Strength"", Float) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include ""UnityCG.cginc""

            sampler2D _MainTex;
            float4 _Center;
            float _Strength;

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 c = _Center.xy;
                float2 dir = i.uv - c;
                float s = _Strength / 8.0;
                fixed4 col = tex2D(_MainTex, i.uv);
                col += tex2D(_MainTex, i.uv - dir * s * 1.0);
                col += tex2D(_MainTex, i.uv - dir * s * 2.0);
                col += tex2D(_MainTex, i.uv - dir * s * 3.0);
                col += tex2D(_MainTex, i.uv - dir * s * 4.0);
                col += tex2D(_MainTex, i.uv - dir * s * 5.0);
                col += tex2D(_MainTex, i.uv - dir * s * 6.0);
                col += tex2D(_MainTex, i.uv - dir * s * 7.0);
                return col / 8.0;
            }
            ENDCG
        }
    }
    Fallback Off
}
";

        private void OnEnable()
        {
            EnsureMaterial();
        }

        private void OnDisable()
        {
            if (_mat != null)
            {
                Destroy(_mat);
                _mat = null;
            }
        }

        private bool EnsureMaterial()
        {
            if (_mat != null) return true;

            if (_cachedShader == null)
            {
                // 既にコンパイル済みのものがあれば探す
                _cachedShader = Shader.Find("Hidden/AstralRadialBlur");
            }

            if (_cachedShader == null)
            {
                try
                {
                    // 1. Shader.Find で標準互換シェーダーを探す (フォールバック)
                    Shader builtin = Shader.Find("Hidden/Internal-Colored");
                    if (builtin != null)
                    {
                        // 描画はできるが、ラジアルブラーは効かない
                        _cachedShader = builtin;
                        SimpleFileLogger.Warn("RadialBlur: custom shader unavailable, using Internal-Colored fallback (effect will be passthrough)");
                    }
                }
                catch (System.Exception ex)
                {
                    SimpleFileLogger.Warn("RadialBlur shader find error: " + ex.Message);
                }
            }

            if (_cachedShader == null)
            {
                SimpleFileLogger.Warn("RadialBlur: no shader available");
                return false;
            }

            _mat = new Material(_cachedShader) { hideFlags = HideFlags.DontSave };
            return true;
        }

        private void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (Strength <= 0.0001f || !EnsureMaterial() || _cachedShader.name != "Hidden/AstralRadialBlur")
            {
                Graphics.Blit(src, dst);
                return;
            }

            _mat.SetVector("_Center", new Vector4(Center.x, Center.y, 0, 0));
            _mat.SetFloat("_Strength", Strength);
            Graphics.Blit(src, dst, _mat);
        }
    }
}
