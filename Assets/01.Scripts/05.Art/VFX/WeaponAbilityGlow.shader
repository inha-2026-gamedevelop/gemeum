Shader "Geumeum/WeaponAbilityGlow"
{
    Properties { [HDR] _Color ("Color", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; float3 uv:TEXCOORD0; float4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 uv:TEXCOORD0; float4 color:COLOR; };
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS=TransformObjectToHClip(v.positionOS.xyz);
                o.uv=v.uv;
                o.color=v.color;
                return o;
            }
            half4 Frag(Varyings v):SV_Target
            {
                float2 p=v.uv.xy*2-1;
                float edge=saturate(1-abs(p.y));
                // z=0은 리본, 1은 잔광, 2는 불꽃, 3은 끊어진 광륜
                if(v.uv.z>.5)
                {
                    float radius=length(p);
                    float grain=frac(sin(dot(floor(v.uv.xy*35),float2(12.9898,78.233)))*43758.5453);
                    float angle=atan2(p.y,p.x);
                    float broken=.45+.55*smoothstep(-.65,.75,sin(angle*7+radius*21));
                    edge=v.uv.z>2.5 ? saturate(1-abs(radius-.78)*14)*broken :
                        v.uv.z>1.5 ? pow(saturate(1-abs(p.x)),.65)*pow(saturate(1-abs(p.y)),2) :
                        pow(saturate(1-radius),2)*lerp(.7,1,grain);
                }
                float core=pow(edge,6);
                return half4(_Color.rgb*v.color.rgb*(.65+core*.55),_Color.a*v.color.a*edge*edge);
            }
            ENDHLSL
        }
    }
}
