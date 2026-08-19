Shader "Fodinae/UI/UnpremultiplyAlpha"
{
    // Converts a premultiplied-alpha render target into straight (unassociated)
    // alpha.
    //
    // The scenery camera has to composite internally with premultiplied alpha -
    // that is the only operator that lets the atmosphere both add in-scattered
    // light and attenuate the crust behind it in a single pass. But UI Toolkit
    // draws its Image elements with a plain SrcAlpha / OneMinusSrcAlpha blend,
    // which multiplies by alpha a second time. The opaque disc (alpha 1) is
    // unaffected, so the error is invisible there, while the atmosphere limb at
    // alpha ~0.2 came out at ~0.04 of its intended brightness - i.e. the
    // atmosphere was present in the render target and then almost entirely
    // erased by the UI blend.
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

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
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                // Fully transparent texels carry no colour to recover, and the
                // guard keeps the divide from exploding there.
                c.rgb /= max(c.a, 1e-4);
                return c;
            }
            ENDHLSL
        }
    }
}
