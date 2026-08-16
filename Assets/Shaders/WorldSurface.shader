Shader "Fodinae/World Surface"
{
    Properties
    {
        [MainTexture] _BaseMap ("Surface Texture", 2D) = "white" {}
        [Toggle] _WrapBaseMap ("Wrap Surface Texture", Float) = 0
        [HDR] _EmissionColor ("Emission Color", Color) = (1, 1, 1, 1)
        _EmissionStrength ("Emission Strength", Range(0, 8)) = 1
        _Occupancy ("Physical Occupancy", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Universal2D"
            Tags { "LightMode" = "Universal2D" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex VisibleVert
            #pragma fragment VisibleFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 glowData : TEXCOORD6;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 worldPosition : TEXCOORD1;
                float emissionMask : TEXCOORD2;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            Texture2D<float4> _WorldLightTexture;
            SamplerState sampler_WorldLightTexture;
            float4 _WorldLightRect;
            float4 _WorldLightTextureSize;
            float _WorldEmissionScale;
            int _WorldLightDebugView;

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _EmissionColor;
                float _EmissionStrength;
                float _Occupancy;
                float _WrapBaseMap;
            CBUFFER_END

            float3 SampleWorldLight(float2 worldPosition)
            {
                float2 lightUV = (worldPosition - _WorldLightRect.xy) / _WorldLightRect.zw;
                if (_WorldLightDebugView != 0)
                {
                    int2 debugPixel = clamp(
                        int2(lightUV * _WorldLightTextureSize.xy),
                        int2(0, 0),
                        int2(_WorldLightTextureSize.xy) - 1);
                    return _WorldLightTexture.Load(int3(debugPixel, 0)).rgb;
                }

                return _WorldLightTexture.Sample(sampler_WorldLightTexture, lightUV).rgb;
            }

            Varyings VisibleVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.worldPosition = TransformObjectToWorld(input.positionOS.xyz).xy;
                output.emissionMask = saturate(frac(input.glowData.y) * 4.0);
                return output;
            }

            half4 VisibleFrag(Varyings input) : SV_Target
            {
                float2 baseMapUV = _WrapBaseMap > 0.5 ? frac(input.uv) : input.uv;
                half4 surface = _BaseMap.SampleLevel(sampler_PointClamp, baseMapUV, 0);
                if (_WorldLightDebugView != 0)
                {
                    return half4(SampleWorldLight(input.worldPosition), surface.a);
                }

                float3 emission = surface.rgb * _EmissionColor.rgb *
                    _EmissionStrength * input.emissionMask * _WorldEmissionScale;
                float3 litSurface = surface.rgb * SampleWorldLight(input.worldPosition);
                return half4(litSurface + emission, surface.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "LightingMaterialField"
            Tags { "LightMode" = "FodinaeLightingMaterialField" }

            Blend One One
            BlendOp Max
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex LightingFieldVert
            #pragma fragment LightingFieldFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 lightingData : TEXCOORD6;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float emissionMask : TEXCOORD1;
            };

            struct LightingFieldOutput
            {
                half4 material : SV_Target0;
                half4 emission : SV_Target1;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _EmissionColor;
                float _EmissionStrength;
                float _Occupancy;
                float _WrapBaseMap;
            CBUFFER_END

            Varyings LightingFieldVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.emissionMask = saturate(frac(input.lightingData.y) * 4.0);
                return output;
            }

            LightingFieldOutput LightingFieldFrag(Varyings input)
            {
                float2 baseMapUV = _WrapBaseMap > 0.5 ? frac(input.uv) : input.uv;
                half4 surface = _BaseMap.SampleLevel(sampler_PointClamp, baseMapUV, 0);
                float coverage = step(0.05, surface.a);
                float occupancy = coverage * surface.a * _Occupancy;
                float emissionStrength = coverage * surface.a *
                    _EmissionStrength * input.emissionMask;

                LightingFieldOutput output;
                output.material = half4(surface.rgb * coverage, occupancy);
                output.emission = half4(
                    surface.rgb * _EmissionColor.rgb * emissionStrength,
                    emissionStrength);
                return output;
            }
            ENDHLSL
        }
    }
}
