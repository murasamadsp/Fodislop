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
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
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
                float2 tileSizeUV   : TEXCOORD2; // [tileU, tileV]
                float2 worldPosAttr : TEXCOORD3; // [serverX, serverY]
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
                output.tileSizeUV = input.tileSizeUV;
                output.worldPos = input.worldPosAttr;
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                float2 baseUV = input.subAtlasRect.xy;
                float2 subAtlasSizeUV = input.subAtlasRect.zw;
                float2 tileSizeUV = input.tileSizeUV;

                // Calculate number of tiles in the sub-atlas (frame)
                // Use round and max to avoid precision issues and division by zero
                float2 tilesCount = round(subAtlasSizeUV / tileSizeUV);
                tilesCount = max(tilesCount, 1.0);

                // Use integer coordinates for wrapping to match CPU logic
                int globalX = (int)floor(input.worldPos.x + 0.001);
                int globalY = (int)floor(input.worldPos.y + 0.001);

                // Wrapping logic matching TextureAtlas.cs
                int wrappedX = ((globalX % (int)tilesCount.x) + (int)tilesCount.x) % (int)tilesCount.x;

                // TextureAtlas.cs: int wrappedY = (tilesPerColumn - 1) - (((globalY % tilesPerColumn) + tilesPerColumn) % tilesPerColumn);
                int wrappedY = ((int)tilesCount.y - 1) - (((globalY % (int)tilesCount.y) + (int)tilesCount.y) % (int)tilesCount.y);

                float2 tileOffsetUV = float2(wrappedX, wrappedY) * tileSizeUV;

                // Sample atlas: base + offset + relative_uv_within_tile
                float2 finalUV = baseUV + tileOffsetUV + input.uv * tileSizeUV;

                return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, finalUV);
            }
            ENDHLSL
        }
    }
}
