#ifndef KERN_SURFACE_GEOLOGY_INCLUDED
#define KERN_SURFACE_GEOLOGY_INCLUDED

// Shared scene-linear reflectance contract for the weathered crust.
// World Y points up; depth and widths are in cells. Emission and occupancy
// are intentionally separate from this albedo operation.
float3 KernWeatheredSurfaceAlbedoTint(float3 albedo, float2 worldPosition, float worldHeight, float3 rockTint)
{
    float depth = worldHeight - worldPosition.y;
    float weathering = 1.0 - smoothstep(2.0, 12.0, depth);
    float luminance = dot(albedo, float3(0.2126, 0.7152, 0.0722));
    float3 basalt = lerp(albedo, luminance.xxx, 0.8) * rockTint;
    return lerp(albedo, basalt, weathering);
}

// Iron-rich red crust: the same reflectance is used by the visible surface
// and its static material field. Values are scene-linear albedo, not emission.
float3 KernWeatheredSurfaceAlbedo(float3 albedo, float2 worldPosition, float worldHeight)
{
    return KernWeatheredSurfaceAlbedoTint(albedo, worldPosition, worldHeight, float3(0.72, 0.26, 0.12));
}

float2 KernSurfaceAlbedoCoordinates(float2 worldPosition)
{
    // Continuous world coordinates: no camera-relative horizontal road texture.
    return worldPosition * 0.25;
}

float KernSurfaceAlbedoHash(float2 key)
{
    float3 p = frac(float3(key.xyx) * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}

float4 KernSampleSurfaceBasalt(TEXTURE2D_PARAM(albedoTexture, albedoSampler),
    float2 worldUV, float seed, float mipBias, bool explicitMip)
{
    float2 cell = floor(worldUV);
    float2 fraction = frac(worldUV);
    float2 blend = fraction * fraction * (3.0 - 2.0 * fraction);
    float4 samples[4];
    [unroll] for (int corner = 0; corner < 4; corner++)
    {
        float2 offset = float2(corner & 1, corner >> 1);
        float2 key = cell + offset + seed;
        float angle = KernSurfaceAlbedoHash(key) * 6.2831853;
        float sine, cosine;
        sincos(angle, sine, cosine);
        float2 relative = worldUV - cell - offset;
        float2 rotated = float2(cosine * relative.x - sine * relative.y,
            sine * relative.x + cosine * relative.y);
        float2 translation = float2(KernSurfaceAlbedoHash(key + 17.7), KernSurfaceAlbedoHash(key + 91.3));
        if (explicitMip)
        {
            samples[corner] = SAMPLE_TEXTURE2D_LOD(albedoTexture, albedoSampler, rotated + translation, 0);
        }
        else
        {
            samples[corner] = SAMPLE_TEXTURE2D_BIAS(albedoTexture, albedoSampler, rotated + translation, mipBias);
        }
    }
    return lerp(lerp(samples[0], samples[1], blend.x), lerp(samples[2], samples[3], blend.x), blend.y);
}


float3 KernComposeSurfaceCrust(TEXTURE2D_PARAM(albedoTexture, albedoSampler),
    float3 originalAlbedo, float2 worldPosition, float worldHeight, float seed,
    bool explicitMip, bool emissive)
{
    float depth = worldHeight - worldPosition.y;
    float weathering = 1.0 - smoothstep(2.0, 12.0, depth);
    if (weathering <= 0.0 || emissive)
    {
        return originalAlbedo;
    }
    float3 basalt = KernSampleSurfaceBasalt(TEXTURE2D_ARGS(albedoTexture, albedoSampler),
        KernSurfaceAlbedoCoordinates(worldPosition), seed, 0.0, explicitMip).rgb;
    float3 crust = lerp(originalAlbedo, basalt, weathering);
    return KernWeatheredSurfaceAlbedo(crust, worldPosition, worldHeight);
}
#endif
