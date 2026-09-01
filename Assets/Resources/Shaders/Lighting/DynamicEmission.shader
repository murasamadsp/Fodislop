Shader "Hidden/Fodinae/DynamicEmission"
{
    // Rasterizes dynamic light sources into the emission field, once per solve.
    //
    // This work used to live inside SampleEmission in WorldLighting.compute,
    // which the ray march calls on every single step - so the light loop ran
    // once per ray step per light. At the measured ~238M ray steps per solve
    // that is 238M iterations for a single lamp, and it scaled linearly with the
    // number of lights in view. Rasterizing the same falloff into the field is
    // the same function evaluated once per covered field texel instead: the
    // quad is only twice the light radius across, so a lamp costs a few hundred
    // pixels rather than hundreds of millions of loop iterations.
    //
    // The falloff below is deliberately identical to the loop it replaces, so
    // this is a pure cost change and not a look change. The march samples the
    // field bilinearly at the same positions it used to evaluate the loop at.
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
                float2 worldPosition : TEXCOORD0;
                nointerpolation float4 colorIntensity : TEXCOORD1;
                nointerpolation float3 centerRadius : TEXCOORD2;
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

                // Matches the compute shader's clamp exactly. Sources are
                // uploaded with a radius of zero, so in practice every light is
                // the four-cell minimum - but reading the field keeps the two
                // implementations from drifting if that ever changes.
                float radius = max(light.positionRadius.w, 4.0 * _CellSize);
                float2 corner = corners[vertexId];
                float2 worldPosition = light.positionRadius.xy + corner * radius;

                Varyings output;
                output.positionCS = TransformWorldToHClip(float3(worldPosition, 0.0));
                output.worldPosition = worldPosition;
                output.colorIntensity = light.colorIntensity;
                output.centerRadius = float3(light.positionRadius.xy, radius);
                return output;
            }

            half4 DynamicEmissionFrag(Varyings input) : SV_Target
            {
                float radius = input.centerRadius.z;
                float dist = length(input.worldPosition - input.centerRadius.xy);

                // Inverse-square law falloff with smooth radius windowing (Karis / Frostbite)
                float d = dist / max(_CellSize, 0.001);
                float distRatio = dist / max(radius, 0.001);
                float window = saturate(1.0 - (distRatio * distRatio * distRatio * distRatio));
                float smoothWindow = window * window;

                // Затухание 1/(d^1.5 + 1) вместо inverse-square 1/(d²+1):
                // согласовано с Transmission в WorldLighting.compute, чтобы
                // источники и марш не расходились по дальности света.
                float atten = smoothWindow / (pow(d, 1.5) + 1.0);

                // Alpha stays zero: the pass blends One One into a field whose
                // alpha the ray march never reads, and adding coverage there
                // would corrupt a channel this shader has no business touching.
                return half4(input.colorIntensity.rgb * (input.colorIntensity.a * atten), 0.0);
            }
            ENDHLSL
        }
    }
}
