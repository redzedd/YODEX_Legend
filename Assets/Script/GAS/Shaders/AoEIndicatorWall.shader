Shader "GAS/AoEIndicatorWall"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 0.8, 0.2, 1)
        _MainTex ("Pattern (R = brightness)", 2D) = "white" {}
        _MainTex_Tiling ("Pattern Tiling (XY)", Vector) = (1, 1, 0, 0)
        _ScrollSpeed ("Scroll Speed Y (+up)", Float) = 0.6
        _DistortionAmount ("UV Distortion Amount", Range(0, 0.5)) = 0.05
        _DistortionFrequency ("UV Distortion Frequency", Range(0, 30)) = 6
        _DistortionSpeed ("UV Distortion Speed", Range(0, 10)) = 2
        _FadeTopPower ("Top Fade Power", Range(0.3, 8)) = 1.4
        _BottomGlowSize ("Bottom Glow Size (0~0.5)", Range(0.001, 0.5)) = 0.08
        _BottomGlowIntensity ("Bottom Glow Intensity", Range(0, 20)) = 10
        _Intensity ("Overall Intensity", Range(0, 8)) = 2.5
        _AlphaIntensity ("Alpha Intensity", Range(0, 5)) = 1.2
        _FadeMultiplier ("Fade Multiplier (master 0~N)", Range(0, 5)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+50"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha One       // 加成混合 — 多層疊加會更亮,適合能量/光效
            ZWrite Off
            Cull Off                 // 雙面渲染 — 玩家從圓柱內外都看得到

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _MainTex_ST;
                float4 _MainTex_Tiling;
                float _ScrollSpeed;
                float _DistortionAmount;
                float _DistortionFrequency;
                float _DistortionSpeed;
                float _FadeTopPower;
                float _BottomGlowSize;
                float _BottomGlowIntensity;
                float _Intensity;
                float _AlphaIntensity;
                float _FadeMultiplier;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 baseUV = IN.uv;
                float2 patternUV = baseUV * _MainTex_Tiling.xy;
                patternUV.y -= _Time.y * _ScrollSpeed;

                // UV 水平方向波浪扭曲 — 仿熱浪/水波感
                float distortion = sin(baseUV.y * _DistortionFrequency + _Time.y * _DistortionSpeed) * _DistortionAmount;
                patternUV.x += distortion;

                half pattern = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, patternUV).r;

                // 上方淡出
                float yFade = saturate(1.0 - pow(saturate(baseUV.y), _FadeTopPower));

                // 底部亮環(uv.y 接近 0 時 boost)
                float bottomGlow = (1.0 - smoothstep(0, _BottomGlowSize, baseUV.y)) * _BottomGlowIntensity;

                // 整體亮度 — _FadeMultiplier 是 master 控制,讓 animator 一個 property 就能 fade 全部
                float brightness = (pattern * _Intensity * yFade + bottomGlow * yFade) * _FadeMultiplier;
                half3 col = _BaseColor.rgb * brightness;
                half alpha = saturate(brightness * _AlphaIntensity) * _BaseColor.a;

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
