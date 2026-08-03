Shader "Hidden/Fodinae/DynamicEmission"
{
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
                float2 localPosition : TEXCOORD0;
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
                float2 corner = corners[vertexId];
                float2 worldPosition = light.positionRadius.xy + corner * (_CellSize * 0.5);

                Varyings output;
                output.positionCS = TransformWorldToHClip(float3(worldPosition, 0.0));
                output.localPosition = corner;
                output.colorIntensity = light.colorIntensity;
                return output;
            }

            half4 DynamicEmissionFrag(Varyings input) : SV_Target
            {
                // A robot is a one-cell emission source. Its power is carried
                // by intensity; propagation and attenuation are solved by
                // the same cascade used for glowing terrain.
                return half4(
                    input.colorIntensity.rgb * input.colorIntensity.a,
                    1.0);
            }
            ENDHLSL
        }
    }
}
