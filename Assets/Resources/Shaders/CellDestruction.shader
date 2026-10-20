Shader "Kern/Cell Destruction"
{
    Properties
    {
        [PerRendererData] _MainTex ("Linear premultiplied emission", 2D) = "black" {}
        [HideInInspector] _SourceTextureSize ("Source texture dimensions", Vector) = (256, 32, 0, 0)
    }
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "CanUseSpriteAtlas" = "True"
        }
        Pass
        {
            Name "Universal2D"
            Tags { "LightMode" = "Universal2D" }
            Cull Off
            ZWrite Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/World/WorldRenderGrid.hlsl"
            #include "Assets/Shaders/PixelArtFiltering.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _SourceTextureSize;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = KernWorldGridVertex(TransformObjectToWorld(input.positionOS));
                output.positionCS = KernWorldGridClipPosition(world);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                // Linear premultiplied emission was imported on the CPU.
                // Alpha is coverage; never multiply RGB by it a second time.
                half4 emission = PixelArtSample(TEXTURE2D_ARGS(_MainTex, sampler_MainTex),
                    input.uv, _SourceTextureSize.xy);
                emission.rgb *= input.color.rgb * input.color.a;
                emission.a *= input.color.a;
                return emission;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
