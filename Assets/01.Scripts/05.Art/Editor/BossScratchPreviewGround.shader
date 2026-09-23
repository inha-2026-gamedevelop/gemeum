Shader "Hidden/Geumeum/BossScratchPreviewGround"
{
    Properties
    {
        _Tile ("Tile size", Float) = 1.8
        _Seam ("Seam width", Float) = 0.035
        _BaseColor ("Base color", Color) = (0.42, 0.43, 0.46, 1)
        _Ambient ("Ambient", Color) = (0.055, 0.058, 0.07, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Tile;
                float _Seam;
                half4 _BaseColor;
                half4 _Ambient;
            CBUFFER_END

            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; float3 normal:TEXCOORD1; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.world = TransformObjectToWorld(v.positionOS.xyz);
                o.normal = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 Frag(Varyings v):SV_Target
            {
                float2 cell = v.world.xz / max(0.05, _Tile);
                float checker = fmod(abs(floor(cell.x) + floor(cell.y)), 2);
                float2 f = abs(frac(cell) - 0.5);
                float seam = saturate(max(f.x, f.y) - (0.5 - _Seam)) / max(1e-4, _Seam);
                float3 albedo = _BaseColor.rgb * lerp(0.88, 1.08, checker);
                albedo = lerp(albedo, albedo * 0.38, saturate(seam));

                float3 n = normalize(v.normal);
                Light main = GetMainLight();
                float3 lit = main.color * saturate(dot(n, main.direction));

                #ifdef _ADDITIONAL_LIGHTS
                uint count = GetAdditionalLightsCount();
                for (uint i = 0u; i < count; i++)
                {
                    Light add = GetAdditionalLight(i, v.world);
                    lit += add.color * add.distanceAttenuation * saturate(dot(n, add.direction));
                }
                #endif

                return half4(albedo * (lit + _Ambient.rgb), 1);
            }
            ENDHLSL
        }
    }
}
