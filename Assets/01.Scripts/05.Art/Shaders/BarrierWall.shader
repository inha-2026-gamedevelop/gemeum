// 결계 벽 셸. 구매 에셋 Bubble S 2(Shader Graphs/Bubble Standard Shield)는 구(Sphere) UV를 전제로 해
// 원통에 올리면 불투명 덩어리가 되거나 텍스처가 완전히 소멸한다. 그래서 같은 노이즈(Noise 30)와 같은
// 색 값만 가져오고 매핑은 원통 UV에 맞춰 다시 짰다.
// Cull Off라 결계 안쪽에서도 보인다. 프레넬은 abs(dot)을 써서 앞면·뒷면 모두 테두리가 살아난다
Shader "Geumeum/BarrierWall"
{
    Properties
    {
        _MainTex          ("Noise", 2D)                    = "white" {}
        [HDR] _TextureColor   ("Texture Color", Color)     = (0.4946, 0.1764, 0.9215, 1)
        _TextureOpacity   ("Texture Opacity", Range(0, 4)) = 1.86
        // 노이즈가 어두운 구간은 알파가 0에 가까워져 벽이 뚫린 것처럼 비친다.
        // 이 값이 그 바닥을 받쳐 최소한의 벽 두께를 보장한다
        _BaseOpacity      ("Base Opacity", Range(0, 1))    = 0.12
        _Tiling           ("Tiling (XY)", Vector)          = (24, 3, 0, 0)
        _ScrollSpeed      ("Scroll Speed (XY)", Vector)    = (0.02, -0.1, 0, 0)
        // 노이즈를 한 번만 깔면 타일 격자가 눈에 보인다. 배율이 정수비가 아닌 두 번째 층을
        // 반대 방향으로 흘려 겹치면 반복 주기가 길어져 격자가 사라진다. 0이면 예전처럼 한 층만 쓴다
        _DetailRatio      ("Detail Ratio", Range(0.1, 1))  = 0.37
        _DetailWeight     ("Detail Weight", Range(0, 1))   = 0.5
        [HDR] _FresnelColor   ("Fresnel Color", Color)     = (0.5304, 0.2265, 1.8720, 1)
        _FresnelPower     ("Fresnel Power", Float)         = 1.5
        _BottomFade       ("Bottom Fade", Range(0, 1))     = 0.08
        _TopFade          ("Top Fade", Range(0, 1))        = 0.35
        _Alpha            ("Master Alpha", Range(0, 1))    = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "BarrierWallUnlit"
            Tags { "LightMode" = "UniversalForward" }

            // premultiplied alpha. 프래그먼트가 rgb에 미리 알파를 곱해서 내보낸다
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _TextureColor;
                float4 _FresnelColor;
                float4 _Tiling;
                float4 _ScrollSpeed;
                float  _DetailRatio;
                float  _DetailWeight;
                float  _TextureOpacity;
                float  _BaseOpacity;
                float  _FresnelPower;
                float  _BottomFade;
                float  _TopFade;
                float  _Alpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 viewDirWS   : TEXCOORD2;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;

                VertexPositionInputs positionInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   normalInputs   = GetVertexNormalInputs(IN.normalOS);

                OUT.positionHCS = positionInputs.positionCS;
                OUT.uv          = IN.uv;
                OUT.normalWS    = normalInputs.normalWS;
                OUT.viewDirWS   = GetWorldSpaceViewDir(positionInputs.positionWS);

                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                // 원통 UV. u는 둘레, v는 높이다. 스케일이 반경에 따라 변해도 타일링은 고정이라
                // 결계가 좁아질수록 무늬가 촘촘해 보인다 — 축소를 눈으로 알아채게 하는 의도된 동작
                float2 uv     = (IN.uv * _Tiling.xy) + (_Time.y * _ScrollSpeed.xy);
                float  noise  = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).r;

                // 두 번째 층. 배율을 정수비가 아닌 값으로 낮추고 반대 방향으로 흘려
                // 두 무늬가 같은 자리에서 다시 만나기까지의 주기를 길게 만든다
                float2 detailUV    = (IN.uv * _Tiling.xy * _DetailRatio) - (_Time.y * _ScrollSpeed.xy * 0.6);
                float  detailNoise = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, detailUV).r;
                noise = lerp(noise, noise * detailNoise * 2.0, _DetailWeight);

                float3 normalWS  = normalize(IN.normalWS);
                float3 viewDirWS = normalize(IN.viewDirWS);

                // abs를 쓰지 않으면 Cull Off로 그려지는 뒷면에서 프레넬이 뒤집혀 테두리가 반대로 뜬다
                float fresnel = pow(saturate(1.0 - abs(dot(normalWS, viewDirWS))), _FresnelPower);

                // 위아래 페이드를 정점 알파가 아니라 UV.y로 처리한다. 정점 링이 위아래 두 개뿐이라
                // 정점 색으로 하면 모든 정점이 페이드 대상이 돼 벽이 통째로 사라진다
                float bottomMask = smoothstep(0.0, max(_BottomFade, 1e-4), IN.uv.y);
                float topMask    = 1.0 - smoothstep(1.0 - max(_TopFade, 1e-4), 1.0, IN.uv.y);
                float verticalFade = bottomMask * topMask;

                // _BaseOpacity가 알파의 바닥을 잡는다. 이게 없으면 노이즈가 어두운 구간에서
                // 알파가 0으로 떨어져 벽이 얼룩덜룩 뚫린 것처럼 보인다
                float  alpha = saturate(_BaseOpacity + (noise * _TextureOpacity) + fresnel)
                             * verticalFade * _Alpha;

                // 바닥 몫은 색도 같이 받쳐 줘야 알파만 오르고 색이 비는 현상이 없다
                float3 color = (_TextureColor.rgb * (_BaseOpacity + noise * _TextureOpacity))
                             + (_FresnelColor.rgb * fresnel);

                return half4(color * verticalFade * _Alpha, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
