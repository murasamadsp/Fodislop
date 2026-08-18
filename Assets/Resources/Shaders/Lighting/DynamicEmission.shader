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
                nointerpolation float2 sourceFraction : TEXCOORD2;
                nointerpolation float2 basePixel : TEXCOORD3;
            };

            StructuredBuffer<DynamicLight> _DynamicLights;
            float4 _WorldRect;
            float4 _FieldSize;

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
                float2 fieldPosition =
                    ((light.positionRadius.xy - _WorldRect.xy) / _WorldRect.zw) *
                    _FieldSize.xy - 0.5;
                float2 basePixel = floor(fieldPosition);
                float2 pixelWorldSize = _WorldRect.zw / _FieldSize.xy;
                float2 worldCenter = _WorldRect.xy +
                    (basePixel + 1.0) * pixelWorldSize;
                float2 worldPosition = worldCenter + corner * pixelWorldSize;

                Varyings output;
                output.positionCS = TransformWorldToHClip(float3(worldPosition, 0.0));
                output.localPosition = corner;
                output.colorIntensity = light.colorIntensity;
                output.sourceFraction = frac(fieldPosition);
                output.basePixel = basePixel;
                return output;
            }

            half4 DynamicEmissionFrag(Varyings input) : SV_Target
            {
                float2 pixelIndex = floor(input.positionCS.xy) - input.basePixel;
                float2 xWeights = lerp(
                    float2(1.0, 0.0),
                    float2(0.0, 1.0),
                    input.sourceFraction.x);
                float2 yWeights = lerp(
                    float2(1.0, 0.0),
                    float2(0.0, 1.0),
                    input.sourceFraction.y);
                float weight =
                    (pixelIndex.x < 0.5 ? xWeights.x : xWeights.y) *
                    (pixelIndex.y < 0.5 ? yWeights.x : yWeights.y);
                return half4(
                    input.colorIntensity.rgb * input.colorIntensity.a * weight,
                    1.0);
            }
            ENDHLSL
        }
    }
}
