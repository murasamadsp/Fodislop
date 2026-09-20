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
                float2 geometrySample : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float2 corner = round(input.uv);
                float2 geometry = corner.x < 0.5
                    ? (corner.y < 0.5
                        ? float2(_CornersX.x, _CornersY.x)
                        : float2(_CornersX.w, _CornersY.w))
                    : (corner.y < 0.5
                        ? float2(_CornersX.y, _CornersY.y)
                        : float2(_CornersX.z, _CornersY.z));
                output.positionCS = TransformObjectToHClip(
                    float3((geometry * 2.0) - 1.0, 0.0));
                output.uv = input.uv;
                output.geometrySample = geometry;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float coverage = EvaluateTerrainCellCoverage(
                    input.geometrySample,
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
                float2 geometrySample : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float2 corner = round(input.uv);
                float2 geometry = corner.x < 0.5
                    ? (corner.y < 0.5
                        ? float2(_CornersX.x, _CornersY.x)
                        : float2(_CornersX.w, _CornersY.w))
                    : (corner.y < 0.5
                        ? float2(_CornersX.y, _CornersY.y)
                        : float2(_CornersX.z, _CornersY.z));
                output.positionCS = TransformObjectToHClip(
                    float3((geometry * 2.0) - 1.0, 0.0));
                output.uv = input.uv;
                output.geometrySample = geometry;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float coverage = EvaluateTerrainCellCoverage(
                    input.geometrySample,
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
