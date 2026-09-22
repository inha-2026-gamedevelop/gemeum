Shader "Geumeum/Environment/BloodMoonSky"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.009,0.007,0.018,1)
        _Horizon ("Horizon", Color) = (0.16,0.024,0.031,1)
        [HDR] _MoonColor ("Moon", Color) = (1.5,0.19,0.065,1)
        _MoonDirection ("Moon direction", Vector) = (0,0.38,1,0)
        _MoonRadius ("Angular radius", Float) = 0.115
        _MoonSurface ("Lunar surface (WeatherMaker)", 2D) = "white" {}
    }
    SubShader
    {
        // WeatherMaker의 스카이 패스 다음에 그리되 깊이 검사로 지형 뒤에만 표시한다.
        Tags { "RenderType"="Transparent" "Queue"="Transparent-100" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Front
            ZWrite Off
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Zenith, _Horizon, _MoonColor, _MoonDirection;
                float _MoonRadius;
            CBUFFER_END
            TEXTURE2D(_MoonSurface);
            SAMPLER(sampler_MoonSurface);
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                // 하늘은 카메라의 원거리 클리핑 값과 무관하게 가장 먼 깊이에 그린다.
                #if UNITY_REVERSED_Z
                    o.positionCS.z = 0.0;
                #else
                    o.positionCS.z = o.positionCS.w;
                #endif
                return o;
            }
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float Noise(float2 p)
            {
                float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);
            }
            float CloudNoise(float3 p)
            {
                // 월드 방향의 연속 노이즈를 써 큐브 면 경계가 드러나지 않게 한다.
                float z=floor(p.z), f=frac(p.z); f=f*f*(3-2*f);
                return lerp(Noise(p.xy+z*float2(17,31)),Noise(p.xy+(z+1)*float2(17,31)),f);
            }
            float Clouds(float3 p)
            {
                return CloudNoise(p)*.53+CloudNoise(p*2.03)*.27
                    +CloudNoise(p*4.11)*.13+CloudNoise(p*8.21)*.07;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float3 d=normalize(i.positionWS-_WorldSpaceCameraPos);
                float3 axis=normalize(_MoonDirection.xyz);
                float alignment=dot(d,axis);
                float radius=max(0.01,_MoonRadius);
                float disc=smoothstep(cos(radius+0.001),cos(radius-0.001),alignment);
                float3 right=normalize(cross(float3(0,1,0),axis));
                float3 up=cross(axis,right);
                float2 uv=float2(dot(d,right),dot(d,up))/radius;
                // 1페이즈와 같은 달 표면을 구면에 투영해 바다와 크레이터를 보존한다.
                float3 lunarNormal=float3(uv,sqrt(saturate(1-dot(uv,uv))));
                float2 lunarUV=float2(atan2(lunarNormal.x,lunarNormal.z)/6.2831853+.5,
                    asin(clamp(lunarNormal.y,-1,1))/3.1415927+.5);
                float3 surface=SAMPLE_TEXTURE2D(_MoonSurface,sampler_MoonSurface,lunarUV).rgb;
                float lighting=saturate(dot(lunarNormal,normalize(float3(-.32,.22,.92))));
                float3 moon=_MoonColor.rgb*surface*(.16+.84*lighting);
                float3 sky=lerp(_Horizon.rgb,_Zenith.rgb,saturate(d.y*1.9+0.08));
                // 느린 이동은 환경 연출뿐이며 전투 예고의 점멸과 혼동되지 않게 한다.
                float3 p=d*7+float3(_Time.y*.002,0,0);
                float cloud=Clouds(p+CloudNoise(p*1.7)*.25);
                float lit=smoothstep(.30,.74,cloud);
                float rim=saturate((CloudNoise(p+float3(.2,.3,0))-cloud)*2);
                float3 cloudColor=lerp(float3(.045,.004,.014),float3(.85,.08,.10),lit);
                cloudColor+=float3(.24,.045,.045)*rim;
                sky=lerp(sky,cloudColor,saturate(d.y*3+.7));
                float halo=pow(saturate(alignment),48)*0.055;
                sky+=_MoonColor.rgb*halo;
                // 달 앞의 옅은 구름도 남겨 천체가 배경 위에 붙인 원판처럼 보이지 않게 한다.
                moon=lerp(moon,cloudColor,smoothstep(.64,.82,cloud)*.30);
                return half4(lerp(sky,moon,disc),1);
            }
            ENDHLSL
        }
    }
}
