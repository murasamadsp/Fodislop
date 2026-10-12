# Terrain raster and contact AO regression

Run `dotnet run --project tools/Kern.TerrainRasterTests/RunRasterTests` (the .NET
SDK, clang++ and the C++ test backend are required).
The local pre-commit hook and the architecture CI job run this check. CI currently
has manual workflow triggers; the hook supplies automatic local coverage.

The runner reads the current production HLSL, adapts vector constructors to the
existing clang float32 shim, and executes `LoadTerrainCellVertex`,
`TerrainGeometryCoverage`, and the AO sampling functions. An independent CPU
triangle rasterizer interpolates the shader outputs. Expectations come from a
convex polygon half-plane oracle sampled at raster positions between logical
pixel centers.

Coverage includes exact displaced polygon edges, adjacent cell seams read
from shared grid nodes of neighbouring cells, background geometry isolation,
shader-computed phase (every k/1000 value and every faceted value against an
independent half truncation), shader-computed grid nodes (classic and organic,
against `TerrainVertexDistortionCalculator.ComputeNode`), per-vertex buffer
reads (at most 28), the ID-mesh corner index order, and the real
`LoadTerrainCellVertex` on three varied worlds exported by the terrain stand
(`ExportWorldsForHlslShim`): every vertex attribute must equal the frozen CPU
reference (`TerrainQuadBuilder.FillQuad` in the test tree), including the
view-offset and door-overlay addresses. Shader mutations of the relief,
occlusion, pack-wall, node jitter sign, organic noise rounding and round-block
rim rules must fail (the round-block rim must follow the round silhouette), displaced autotile UV continuity at a shared
edge, and contact AO shape sensitivity at
8/16/32/64 texels per cell. It verifies AO carrier padding and checks the
signed-distance edge helper against axis-aligned and diagonal distance oracles.
The AO field stores a half-cell contact falloff and screen shading reads one
mip-zero sample. The runner injects a second fragment
quantization and a flat contact response in temporary generated code; both defects
must fail. Repository files are never mutated.
An independent eight-vertex contour checks the organic signed distance, and
the analytic flat-cell distance is checked against the polygon distance.

This is an algorithm regression, not a Unity render test. It cannot validate
Metal compilation, runtime texture bindings, mesh submission order, or the final
camera image. The MainGame PlayMode test checks runtime wiring separately. The
sampling fixture models bilinear mip-zero filtering at the receiver position.
