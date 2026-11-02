Shader "Diverse/EnvironmentParticles"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off ZWrite Off
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            #include "DiversePixel.hlsl"
            struct A { float3 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            V vert(A v) { V o; o.positionCS = TransformObjectToHClip(v.positionOS); o.color = v.color; o.uv = v.uv; return o; }
            half4 frag(V i) : SV_Target { return SamplePixelArt(TEXTURE2D_ARGS(_MainTex, sampler_MainTex), i.uv) * i.color; }
            ENDHLSL
        }
    }
}
