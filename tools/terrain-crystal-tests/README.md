# Original OpenMines shimmer and lava checks

Run `python3 tools/terrain-crystal-tests/run.py`. These are auxiliary CPU checks
of production HLSL, not Unity shader compilation or a visual/GPU regression test.

X-crystals use the OpenMines `Unlit_TerrainShader.shader` branch `animType == 5`
(lines 335–373), with colors/speed from `CellRender.cs` (395–434). The shader
executes the original equation in gamma space; the caller converts the linear
terrain atlas sample to gamma and the result back to linear. This preserves the
original working space while leaving world lighting in its existing space.
No normal maps, derived light directions, extra facet highlights or color masks
are part of this effect.

`PrismaticFlowMap.bytes` is the original 160×128 RGBA8 phase region, extracted
from the 2048×2048 OpenMines terrain atlas: x=400,y=368,width=160,height=128.
Rows are flipped for Unity raw texture loading; no resampling/recoloring occurs.
Regenerate with `python3 tools/terrain-crystal-tests/generate.py` (requires Pillow
and the sibling OpenMines checkout). Tests require neither Pillow nor OpenMines;
they verify the committed phase data hash and compare 12,000 color samples to an
independent Python/colorsys evaluation of the source equation. Wrong Y and wrong
luminance mutations must fail.

Lava scrolls the entire authored sheet and evaluates a separate heat field on
world-anchored 1/32-cell centers. Its local visual speed is always 10, independent
of server sprite-animation metadata (including None/zero). The C# catalog test
covers zero metadata. Heat no longer vanishes on red texels with little green.
The native UV test covers 4,608 shared-edge samples, rejects cell-local addressing,
and checks heat contrast and uniformity inside a 1/32 cell sample.

Cost: crystals retain one phase-map sample (80 KiB, Bilinear, no mipmaps), HSV hue,
one sine, the original polynomial and gamma/linear conversion. The rejected
reflection path's normal-map read, four neighboring light reads, derivatives and
400 KiB texture are removed. Lava uses two sines and scalar arithmetic with zero
flow-map reads. No extra passes, dispatches or per-frame allocations are added.

Remaining validation in Unity: compile both Terrain passes and inspect animated
X-crystals/lava in the running game. The CPU tests do not establish final image
quality, material binding or GPU performance.
