#ifndef FODINAE_PLANET_NOISE_INCLUDED
#define FODINAE_PLANET_NOISE_INCLUDED

// Shared procedural basis for the main-menu planet (surface + atmosphere).
//
// Deliberately NOT the usual frac(sin(dot(p, k)) * 43758.5) hash: that one
// decorrelates poorly and starts visibly banding and self-repeating once the
// sample coordinate grows past a few hundred - which is exactly where the fine
// surface-grain octaves live, and it is what made the previous surface read as
// a smeared, tiled putty rather than rock. pcg3d has no such regime.
uint3 Pcg3d(uint3 v)
{
    v = (v * 1664525u) + 1013904223u;
    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;
    v ^= v >> 16u;
    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;
    return v;
}

// Expects an exact integer lattice coordinate. Returns a gradient vector in
// roughly [-1, 1]^3 (not normalized - unit gradients are not required for
// gradient noise and skipping the rsqrt is measurably cheaper here).
float3 HashGradient(float3 lattice)
{
    uint3 h = Pcg3d(asuint(int3(lattice)));
    return (float3(h) * (2.0 / 4294967295.0)) - 1.0;
}

// Perlin-style gradient noise. Gradient rather than value noise because value
// noise puts its extrema ON the lattice points, which reads as a visible
// axis-aligned grid of blobs when stacked into an fBm - the "quilted" look of
// the old surface.
float GradientNoise(float3 p)
{
    float3 i = floor(p);
    float3 f = p - i;

    // Quintic fade: C2-continuous, so the analytic-difference normals taken in
    // the surface shader do not show facet seams at cell boundaries.
    float3 u = f * f * f * ((f * ((f * 6.0) - 15.0)) + 10.0);

    float n = 0.0;
    [unroll]
    for (int c = 0; c < 8; c++)
    {
        float3 o = float3((c >> 2) & 1, (c >> 1) & 1, c & 1);
        float3 g = HashGradient(i + o);
        float3 d = f - o;
        float3 w3 = lerp(1.0 - u, u, o);
        n += (w3.x * w3.y * w3.z) * dot(g, d);
    }

    // Gradient noise built from components in [-1,1] lands near [-0.7, 0.7];
    // rescale so downstream thresholds can be reasoned about in [-1, 1].
    return saturate((n * 1.4 * 0.5) + 0.5) * 2.0 - 1.0;
}

// Standard fBm, output ~[-1, 1].
float Fbm(float3 p, int octaves)
{
    float sum = 0.0;
    float amp = 0.5;
    float norm = 0.0;

    [loop]
    for (int i = 0; i < octaves; i++)
    {
        sum += amp * GradientNoise(p);
        norm += amp;
        amp *= 0.5;

        // Irrational-ish lacunarity plus a per-octave offset: an exact 2.0 step
        // makes octave extrema line up on the same lattice points and produces
        // a faint but very readable grid.
        p = (p * 2.037) + float3(19.31, 7.53, 13.77);
    }

    return sum / max(norm, 1e-4);
}

// Ridged multifractal, output ~[0, 1] with sharp crests near 1. This is what
// produces connected mountain chains and crack networks - plain fBm only ever
// gives rounded lumps.
float RidgedFbm(float3 p, int octaves)
{
    float sum = 0.0;
    float amp = 0.5;
    float norm = 0.0;
    float prev = 1.0;

    [loop]
    for (int i = 0; i < octaves; i++)
    {
        float r = 1.0 - abs(GradientNoise(p));
        r *= r;

        // Weighting each octave by the previous one concentrates detail onto
        // existing crests instead of sprinkling it uniformly, which is what
        // makes the ridges read as one continuous range rather than noise.
        r *= prev;
        prev = saturate(r * 2.0);

        sum += amp * r;
        norm += amp;
        amp *= 0.5;
        p = (p * 2.037) + float3(5.17, 11.93, 3.71);
    }

    return saturate(sum / max(norm, 1e-4));
}

#endif // FODINAE_PLANET_NOISE_INCLUDED
