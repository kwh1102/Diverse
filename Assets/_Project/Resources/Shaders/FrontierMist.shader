Shader "Diverse/FrontierMist"
{
    Properties
    {
        _MistColor ("Mist colour", Color) = (.44,.47,.59,.94)
        _Bound ("Locked radius", Float) = 100
        _Width ("Mist width", Float) = 5
        _Drift ("Drift", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent+100" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off ZWrite Off
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _MistColor;
                float _Bound, _Width, _Drift;
            CBUFFER_END
            struct A { float3 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float2 world : TEXCOORD0; };
            V vert(A v)
            {
                V o; float3 world = TransformObjectToWorld(v.positionOS);
                o.positionCS = TransformWorldToHClip(world); o.world = world.xy; return o;
            }
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1,311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f*f*(3-2*f);
                return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);
            }
            half4 frag(V i) : SV_Target
            {
                float2 p = floor(i.world * 16) / 16;
                float clouds = noise(p * .65 + float2(_Drift, -_Drift * .3));
                float detail = noise(p * 1.8 - _Drift * .6);
                float d = length(p) - _Bound;
                float edge = d + (clouds - .5) * _Width * .55;
                // The exact collision radius stays inside dense mist; the approach feathers inward.
                float alpha = smoothstep(-_Width, .6, edge) * (.72 + .28 * clouds);
                float rim = 1 - smoothstep(.1, 1.3, abs(d + .35));
                half3 color = _MistColor.rgb + (clouds-.5)*.13 + (detail-.5)*.035 + rim*.09;
                return half4(color, alpha * _MistColor.a);
            }
            ENDHLSL
        }
    }
}
