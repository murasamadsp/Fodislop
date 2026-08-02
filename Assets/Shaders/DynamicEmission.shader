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
                float2 worldPosition = light.positionRadius.xy + corner * light.positionRadius.z;

                Varyings output;
                output.positionCS = TransformWorldToHClip(float3(worldPosition, 0.0));
                output.localPosition = corner;
                output.colorIntensity = light.colorIntensity;
                return output;
            }

            half4 DynamicEmissionFrag(Varyings input) : SV_Target
            {
                float distanceFromCenter = length(input.localPosition);
                float antialias = max(fwidth(distanceFromCenter), 0.0001);
                float sourceShape = 1.0 - smoothstep(
                    1.0 - antialias,
                    1.0 + antialias,
                    distanceFromCenter);
                return half4(
                    input.colorIntensity.rgb * input.colorIntensity.a * sourceShape,
                    sourceShape);
            }
            ENDHLSL
        }
    }
}
