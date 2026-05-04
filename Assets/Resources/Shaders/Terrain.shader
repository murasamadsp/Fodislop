Shader "Universal Render Pipeline/Custom/Terrain"
{
    Properties
    {
        [MainTexture] _BaseMap ("Texture Atlas", 2D) = "white" {}
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend Off
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0; // Quad-relative UV [0,1]
                float4 subAtlasRect : TEXCOORD1; // [minU, minV, sizeU, sizeV] for current frame
                float4 tileSizeUV   : TEXCOORD2; // [tileU, tileV, unused, unused]
                float4 worldPosAttr : TEXCOORD3; // [serverX, serverY, unused, unused]
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float4 subAtlasRect : TEXCOORD1;
                float2 tileSizeUV   : TEXCOORD2;
                float2 worldPos     : TEXCOORD3;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            Varyings vert (Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.subAtlasRect = input.subAtlasRect;
                output.tileSizeUV = input.tileSizeUV.xy;
                output.worldPos = input.worldPosAttr.xy;
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                float2 baseUV = input.subAtlasRect.xy;
                float2 subAtlasSizeUV = input.subAtlasRect.zw;
                float2 tileSizeUV = input.tileSizeUV;

                // Debug: return red if no texture or weird dimensions
                if (subAtlasSizeUV.x <= 0 || tileSizeUV.x <= 0)
                    return half4(1, 0, 0, 1);

                // Calculate number of tiles in the sub-atlas (frame)
                float2 tilesCount = round(subAtlasSizeUV / tileSizeUV);
                tilesCount = max(tilesCount, 1.0);

                // Server-side coordinates
                int globalX = (int)floor(input.worldPos.x + 0.001);
                int globalY = (int)floor(input.worldPos.y + 0.001);

                // Wrapping logic matching TextureAtlas.cs
                int wrappedX = ((globalX % (int)tilesCount.x) + (int)tilesCount.x) % (int)tilesCount.x;
                int wrappedY = ((int)tilesCount.y - 1) - (((globalY % (int)tilesCount.y) + (int)tilesCount.y) % (int)tilesCount.y);

                float2 tileOffsetUV = float2(wrappedX, wrappedY) * tileSizeUV;

                // Sample atlas: base + offset + relative_uv_within_tile
                // Added 0.0001 epsilon to uv to avoid edge bleeding from the wrong tile
                float2 finalUV = baseUV + tileOffsetUV + input.uv * tileSizeUV;

                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, finalUV);

                // If the color is transparent, we might want to discard or use a fallback
                // but since it's opaque now, let's just return it.
                return color;
            }
            ENDHLSL
        }
    }
}
