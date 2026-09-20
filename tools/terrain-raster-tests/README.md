# Terrain raster and contact AO regression

Run `python3 tools/terrain-raster-tests/run.py` (Python 3 and clang++ required).
The local pre-commit hook and the architecture CI job run this check. CI currently
has manual workflow triggers; the hook supplies automatic local coverage.

The runner reads the current production HLSL, adapts vector constructors to the
existing clang float32 shim, and executes `LoadTerrainCellVertex`,
`TerrainGeometryCoverage`, and the AO sampling functions. An independent CPU
triangle rasterizer interpolates the shader outputs. Expectations come from a
convex polygon half-plane oracle sampled at logical pixel centers.

Coverage includes full subpixel coverage outside the original polygon, adjacent
cell seams, background geometry isolation, and contact AO shape sensitivity at
8/16/32/64 texels per cell. AO sampling uses a generated occupancy mip pyramid.
The runner also mutates the carrier and mip choice in temporary generated code:
each old defect must make its test fail. Repository files are never mutated.

This is an algorithm regression, not a Unity render test. It cannot validate
Metal compilation, runtime texture bindings, mesh submission order, or the final
camera image. The MainGame PlayMode test checks runtime wiring separately. The
sampling fixture models trilinear mip selection; the tested power-of-two AO
resolutions select integer mip levels, so no fractional-mip filtering assumption
is needed for those checks.
