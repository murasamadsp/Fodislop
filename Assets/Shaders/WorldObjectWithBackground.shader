Shader "Custom/WorldObjectWithBackground"
{
    Properties
    {
        _MainTex ("Main Texture", 2D) = "white" {}
        _BackgroundTex ("Background Texture", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        [Toggle(_CHECK_BACKGROUND_ON)]_CheckBackground("Check Background", Float) = 1
    }

    SubShader
    {
        Tags {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _CHECK_BACKGROUND_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 bgUV : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 bgUV : TEXCOORD1;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_BackgroundTex);
            SAMPLER(sampler_BackgroundTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _BackgroundTex_ST;
                float _Cutoff;
                float _CheckBackground;
            CBUFFER_END

            sampler2D _WorldLightTexture;
            float4 _WorldLightRect;

            float3 GetTerrariaLightColor(float2 worldPos)
            {
                float2 lightUV = (worldPos - _WorldLightRect.xy) / _WorldLightRect.zw;
                return tex2D(_WorldLightTexture, lightUV).rgb;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                float4 worldPos = mul(UNITY_MATRIX_M, input.positionOS);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.bgUV = TRANSFORM_TEX(input.bgUV, _BackgroundTex);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 mainColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                float3 lightColor = GetTerrariaLightColor(input.positionCS.xy);

                #if _CHECK_BACKGROUND_ON
                half4 bgColor = SAMPLE_TEXTURE2D(_BackgroundTex, sampler_BackgroundTex, input.bgUV);

                if (mainColor.a < _Cutoff)
                {
                    return half4(bgColor.rgb * lightColor, bgColor.a);
                }

                half3 finalColor = lerp(bgColor.rgb, mainColor.rgb, mainColor.a) * lightColor;
                return half4(finalColor, 1.0);
                #else
                return half4(mainColor.rgb * lightColor, mainColor.a);
                #endif
            }
            ENDHLSL
        }
    }
}
