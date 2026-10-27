Shader "Canon/MainGameMilkSpray/LiquidParticle"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                fixed4 color : COLOR;
                float2 glowUv : TEXCOORD0;
                float4 surface : TEXCOORD1; // Gloss, edge softness, emission, halo flag.
            };
            struct v2f
            {
                float4 position : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                half3 worldNormal : TEXCOORD1;
                fixed4 color : COLOR;
                half4 surface : TEXCOORD2;
                half2 glowUv : TEXCOORD3;
            };
            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                output.color = input.color;
                output.surface = input.surface;
                output.glowUv = input.glowUv;
                return output;
            }
            half3 SafeUnit(half3 value)
            {
                return value * rsqrt(max(dot(value, value), 0.000001h));
            }
            half4 frag(v2f input) : SV_Target
            {
                if (input.surface.w > 0.5h)
                {
                    half2 distance = abs(input.glowUv);
                    half2 along = saturate(1.0h - distance);
                    half2 across = 1.0h - smoothstep(0.0h, 0.22h, distance);
                    half crossGlow = (along.x * along.x * across.y + along.y * along.y * across.x) * 0.24h;
                    half halo = saturate(1.0h - length(input.glowUv));
                    half glow = crossGlow + halo * halo * halo * 0.08h;
                    // Alpha zero makes this halo additive under One/OneMinusSrcAlpha blending.
                    // Its RGB still follows the parent opacity and the ghost's full fade.
                    return half4(input.color.rgb * (glow * input.surface.z * input.color.a), 0.0h);
                }
                half3 normal = SafeUnit(input.worldNormal);
                half3 view = SafeUnit(_WorldSpaceCameraPos.xyz - input.worldPosition);
                half facing = saturate(dot(normal, view));
                half softness = saturate(input.surface.y);
                // Exactly zero disables edge fading, including at grazing angles.
                half edge = softness <= 0.0h ? 1.0h : smoothstep(0.0h, max(0.0001h, softness), facing);
                half alpha = saturate(input.color.a) * edge;
                half3 lightDirection = SafeUnit(UnityWorldSpaceLightDir(input.worldPosition));
                half diffuse = saturate(dot(normal, lightDirection));
                half3 ambient = max(ShadeSH9(half4(normal, 1.0h)), half3(0.12h, 0.12h, 0.12h));
                half gloss = saturate(input.surface.x);
                half exponent = lerp(8.0h, 128.0h, gloss * gloss);
                half specular = pow(saturate(dot(normal, SafeUnit(view + lightDirection))), exponent)
                    * (0.04h * (exponent + 2.0h) / 8.0h) * step(0.0001h, diffuse);
                half3 lit = input.color.rgb * (ambient + _LightColor0.rgb * diffuse)
                    + _LightColor0.rgb * specular + input.color.rgb * max(0.0h, input.surface.z);
                // Preserve HDR emission and premultiply all lighting, so nothing survives alpha=0.
                return half4(lit * alpha, alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
