// 검 기본 공격 검기. MeleeSlashView가 실제 검날 경로로 만든 리본을 그린다.
// uv0.x는 점의 나이(0 방금, 1 사라짐), uv0.y는 손잡이 쪽 0에서 칼끝 1, uv0.z는 칼끝이 지나온 거리다.
// 새 점일수록 진하고 칼끝 테두리가 밝으며, 늙은 점은 옅어지면서 노이즈로 갉아먹혀 잔상으로 사라진다.
// 색 문법상 아군 표현이라 몸통은 주묵 붉은색, 앞날은 한지 백색이다. 값은 PresentationConfigSO가 넘긴다
Shader "Geumeum/MeleeSlash"
{
    Properties
    {
        _BodyColor       ("Body Color", Color)               = (0.72, 0.09, 0.07, 0.85)
        [HDR] _EdgeColor ("Edge Color", Color)               = (1.5, 1.42, 1.28, 1)
        _EdgeWidth       ("Edge Width", Range(0.01, 1))      = 0.28
        _HeadGlow        ("Head Glow", Range(0, 2))          = 0.6
        _HeadLength      ("Head Glow Length", Range(0.01, 1)) = 0.2
        _AgeFalloff      ("Age Falloff", Range(0.2, 4))      = 0.8
        _ErodeStart      ("Erode Start Age", Range(0, 0.95)) = 0.45
        _NoiseScale      ("Erosion Noise Scale", Vector)     = (3, 4, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"  = "UniversalPipeline"
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "MeleeSlashUnlit"
            Tags { "LightMode" = "UniversalForward" }

            // premultiplied alpha. 프래그먼트가 rgb에 미리 알파를 곱해서 내보낸다
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BodyColor;
                float4 _EdgeColor;
                float  _EdgeWidth;
                float  _HeadGlow;
                float  _HeadLength;
                float  _AgeFalloff;
                float  _ErodeStart;
                float4 _NoiseScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 trail      : TEXCOORD0;
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 f    = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float a = Hash21(cell);
                float b = Hash21(cell + float2(1.0, 0.0));
                float c = Hash21(cell + float2(0.0, 1.0));
                float d = Hash21(cell + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.trail      = input.uv.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float age      = saturate(input.trail.x);
                float v        = saturate(input.trail.y);
                float distance = input.trail.z;

                float life = pow(saturate(1.0 - age), _AgeFalloff);

                // 몸통은 손잡이 쪽으로 갈수록 옅어지고, 앞날은 칼끝 테두리에 몰린다
                float body     = smoothstep(0.0, 0.7, v);
                float edge     = smoothstep(1.0 - _EdgeWidth, 1.0 - (_EdgeWidth * 0.25), v);
                float headGlow = (1.0 - smoothstep(0.0, _HeadLength, age)) * _HeadGlow;

                // _ErodeStart 이후부터 노이즈 문턱을 올려 갉아먹는다. 그 전까지는 잔상이 온전히 남는다.
                // 거리 좌표라 무늬가 궤적에 붙어 흐르지 않는다. 시작 나이에서는 문턱이 노이즈 최솟값 아래라 구멍이 없다
                float noise     = ValueNoise(float2(distance * _NoiseScale.x, v * _NoiseScale.y));
                float erodeAge  = saturate((age - _ErodeStart) / (1.0 - _ErodeStart));
                float threshold = (erodeAge * 1.2) - 0.2;
                float erode     = smoothstep(threshold, threshold + 0.2, noise);

                float  edgeMix = saturate(edge + (headGlow * 0.5));
                float3 color   = lerp(_BodyColor.rgb, _EdgeColor.rgb, edgeMix);
                float  alpha   = saturate((_BodyColor.a * body) + edge + (headGlow * 0.4));
                alpha *= life * erode;

                return half4(color * alpha, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
