Shader "Hidden/Kern/TerrainQuantizationProbe"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "VisibleTerrain"
            Blend Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/TerrainContour.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CornersX;
                float4 _CornersY;
                float _Anchored;
                float _ApplyGeometry;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float coverage = EvaluateTerrainCellCoverage(
                    input.uv,
                    input.uv,
                    _CornersX,
                    _CornersY,
                    _Anchored,
                    0.0,
                    0.0,
                    1.0,
                    _ApplyGeometry);
                return half4(coverage, coverage, coverage, coverage);
            }
            ENDHLSL
        }

        Pass
        {
            Name "MaterialField"
            Blend Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/TerrainContour.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CornersX;
                float4 _CornersY;
                float _Anchored;
                float _ApplyGeometry;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float coverage = EvaluateTerrainCellCoverage(
                    input.uv,
                    input.uv,
                    _CornersX,
                    _CornersY,
                    _Anchored,
                    0.0,
                    0.0,
                    1.0,
                    _ApplyGeometry);
                return half4(coverage, coverage, coverage, coverage);
            }
            ENDHLSL
        }
    }
}
