Shader "Geumeum/FoxfireGroundVFX"
{
    // 여우불 진(패턴 15) VFX 테스트용 - BossScratchRibbon.shader와 같은 관례(URP 커스텀 HLSL,
    // Blend enum 프로퍼티, HDR 색상, Vertex Color 입력)를 그대로 따른다. 새 shader framework가
    // 아니라 기존 손-작성 shader 계열에 하나 추가하는 것이다.
    //
    // Blender FX_Color 규약: RGB = 실제 색조(이미 절차적 노이즈로 만든 값), A = 가장자리 페이드
    // 마스크. BossScratchRibbon과 달리 RGB를 "코어 가중치"로 재해석하지 않고 그대로 tint로 쓴다 -
    // Blender 쪽 노드가 이미 노이즈*램프*버텍스컬러 결과를 RGB에 구워 놨기 때문이다(README_Unity.md 참고)
    Properties
    {
        _NoiseTex ("Scroll noise (optional, CommonNoise 재사용)", 2D) = "gray" {}
        [HDR] _EmissionTint ("Emission tint (곱)", Color) = (1, 1, 1, 1)
        _EmissionIntensity ("Emission intensity", Range(0, 8)) = 2.0
        _AlphaScale ("Alpha scale", Range(0, 2)) = 1.0
        _NoiseStrength ("Noise strength (0=끔)", Range(0, 1)) = 0.0
        _NoiseTiling ("Noise tiling (u,v)", Vector) = (2, 2, 0, 0)
        _NoiseScrollSpeed ("Noise scroll speed (u,v)", Vector) = (0.05, 0.02, 0, 0)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 10
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            Name "FoxfireGroundVFX"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                half4  _EmissionTint;
                half   _EmissionIntensity;
                half   _AlphaScale;
                half   _NoiseStrength;
                float4 _NoiseTiling;
                float4 _NoiseScrollSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0; // UV0_Flow
                float4 color      : COLOR;     // FX_Color: rgb=tint, a=edge fade
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(posWS);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                // 노이즈는 기본 0(꺼짐) - 우선 Vertex Color + Emission만으로 확인하고,
                // 필요할 때만 _NoiseStrength를 올려 약한 scrolling 흔들림을 더한다
                half noiseMask = 1.0h;
                if (_NoiseStrength > 0.0h)
                {
                    float2 nuv = i.uv * _NoiseTiling.xy + _Time.y * _NoiseScrollSpeed.xy;
                    half n = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, nuv).r;
                    noiseMask = lerp(1.0h, n, _NoiseStrength);
                }

                half3 rgb = i.color.rgb * _EmissionTint.rgb * _EmissionIntensity;
                half alpha = i.color.a * _AlphaScale * noiseMask;

                return half4(rgb, saturate(alpha));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
