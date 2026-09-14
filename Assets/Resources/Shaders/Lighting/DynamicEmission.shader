Shader "Hidden/Fodinae/DynamicEmission"
{
    // Rasterize only the emitting cell. Distance falloff and obstacles belong
    // to the transport solver, not to a radius-shaped field of extra sources.
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Name "DynamicEmission"
            Blend One One
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DynamicEmissionVert
            #pragma fragment DynamicEmissionFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct DynamicLight
            {
                float4 positionRadius;
                float4 colorIntensity;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                nointerpolation float4 colorIntensity : TEXCOORD1;
            };

            StructuredBuffer<DynamicLight> _DynamicLights;
            float _CellSize;

            Varyings DynamicEmissionVert(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
            {
                static const float2 corners[6] =
                {
                    float2(-1.0, -1.0),
                    float2(1.0, -1.0),
                    float2(1.0, 1.0),
                    float2(1.0, 1.0),
                    float2(-1.0, 1.0),
                    float2(-1.0, -1.0),
                };

                DynamicLight light = _DynamicLights[instanceId];

                // The lamp is a one-cell square centred on the robot itself.
                float2 cellCenter = light.positionRadius.xy;
                float2 worldPosition = cellCenter + corners[vertexId] * (0.5 * _CellSize);

                Varyings output;
                output.positionCS = TransformWorldToHClip(float3(worldPosition, 0.0));
                output.colorIntensity = light.colorIntensity;
                return output;
            }

            half4 DynamicEmissionFrag(Varyings input) : SV_Target
            {
                return half4(max(input.colorIntensity.rgb * input.colorIntensity.a, 0.0), 0.0);
            }
            ENDHLSL
        }
    }
}
