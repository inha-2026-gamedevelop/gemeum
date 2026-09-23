Shader "Geumeum/BossScratchRibbon"
{
    Properties
    {
        [MainTexture] _MainTex ("Streak atlas", 2D) = "white" {}
        _NoiseTex ("Noise", 2D) = "gray" {}
        [HDR] _EdgeColor ("Edge color", Color) = (3.2, 0.10, 0.16, 1)
        [HDR] _CoreColor ("Core color", Color) = (9.0, 5.4, 4.4, 1)
        _CoreSharpness ("Core sharpness", Range(0.5, 12)) = 3.2
        _CoreBoost ("Core boost", Range(0, 4)) = 1.0
        _AlphaScale ("Alpha scale", Range(0, 4)) = 1.0
        _NoiseTiling ("Noise tiling", Vector) = (2.5, 1.0, 0, 0)
        _EdgeSoftness ("Lengthwise end softness", Range(0, 0.4)) = 0.05
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+10"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            Name "ScratchRibbon"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);   SAMPLER(sampler_MainTex);
            TEXTURE2D(_NoiseTex);  SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _EdgeColor;
                half4 _CoreColor;
                half _CoreSharpness;
                half _CoreBoost;
                half _AlphaScale;
                float4 _NoiseTiling;
                half _EdgeSoftness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 tangentOS  : NORMAL;     // 리본 진행 방향
                float2 uv         : TEXCOORD0;  // 아틀라스 UV
                float2 shape      : TEXCOORD1;  // x 부호 있는 반폭, y 0=카메라 정면 1=지면 평행
                float2 life       : TEXCOORD2;  // x 노이즈 위상, y 소멸 진행도
                float4 color      : COLOR;      // rgb 코어 가중치, a 세기
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float2 life       : TEXCOORD1;
                float4 color      : COLOR;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 tanWS = normalize(TransformObjectToWorldDir(v.tangentOS));

                float3 viewDir = normalize(GetCameraPositionWS() - posWS);
                float3 sideCam = cross(tanWS, viewDir);
                float3 sideFlat = cross(tanWS, float3(0, 1, 0));
                if (dot(sideCam, sideCam) < 1e-6) sideCam = float3(1, 0, 0);
                if (dot(sideFlat, sideFlat) < 1e-6) sideFlat = float3(1, 0, 0);
                float3 side = normalize(lerp(normalize(sideCam), normalize(sideFlat), saturate(v.shape.y)));

                posWS += side * v.shape.x;
                o.positionCS = TransformWorldToHClip(posWS);
                o.uv = v.uv;
                o.life = v.life;
                o.color = v.color;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half mask = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a;

                float2 nuv = float2(i.uv.x * _NoiseTiling.x + i.life.x,
                                    i.uv.y * _NoiseTiling.y + i.life.x * 0.37);
                half3 n = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, nuv).rgb;

                // 소멸할수록 노이즈를 따라 가닥이 부서진다.
                half erode = saturate(i.life.y);
                half breakup = saturate((n.r * 0.55 + n.g * 0.25 + mask * 0.75 + 0.22 - erode * 1.75) * 3.0);

                half ends = smoothstep(0.0, _EdgeSoftness + 1e-4, i.uv.x)
                          * smoothstep(0.0, _EdgeSoftness + 1e-4, 1.0 - i.uv.x);

                half shaped = mask * breakup * ends;
                half grain = 0.78 + 0.34 * n.b;
                half alpha = saturate(shaped * grain * i.color.a * _AlphaScale);

                half core = pow(saturate(shaped * i.color.r * _CoreBoost), _CoreSharpness);
                half3 rgb = lerp(_EdgeColor.rgb, _CoreColor.rgb, core);

                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
