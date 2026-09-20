# X-crystal phase field

Run `python3 tools/terrain-crystal-tests/run.py`. This is an auxiliary CPU test of
production HLSL, **not** a Unity/GPU/visual regression test. It checks cross-cell
coordinates (including chunk boundaries and negative positions), rejects the
wrong-Y mutation, checks color bounds, dark crevices, temporal variation, and
verifies that the committed numerical texture matches its generator.

`generate.py` bakes a deterministic, periodic 160×128 RGBA8 numerical field.
RG encodes a unit phase vector; B controls sparse local crests; A is unused.
No OpenMines texture bytes are used. Regenerate with `python3 tools/terrain-crystal-tests/generate.py`.
The world period is 10×8 cells. Bilinear repeat sampling interpolates vectors
without the discontinuity of interpolating an angle across its wrap point.
The shader derives facet strength from the actual atlas texel, so rotated and
mirrored terrain tiles retain their authored bright facets.

Cost per affected fragment: one phase texture sample (replaces the existing
flow sample), one sincos, scalar polynomial highlights; no HSV conversion.
The previous effect used one sample, RGB-to-HSV and sin plus white crest math.
No extra draws, dispatches, per-frame texture writes, vertex streams or buffers.
Immutable RGBA8 texture: 81,920 bytes, no mipmaps. FPS is not measured here.

Both Terrain passes bind and sample the same phase texture and invoke the same
color helper. Remaining validation: compile Terrain's Universal2D and field
passes in Unity, then inspect a multi-cell X-crystal patch with rotated atlas
tiles while moving the camera. CPU results do not establish those GPU outcomes.

Lava checks execute `ResolveTerrainTileUV` and `ClampTerrainTileUV` from the
production includes over 4,608 adjacent samples. They vary carrier anchoring,
neighbor tile descriptors, time and negative coordinates. Disabling the molten
sheet path must fail. Lava now samples one scrolling authored sheet; it performs
no additional flow-map read or color overlay. This still requires Unity visual
validation, including filtering at the outer sheet wrap.

The crystal broad band includes dark colored facets (the former linear-luma
threshold excluded roughly half the authored X-crystal sheet); exact black
remains black. The numerical bounds test is not a claim of perceptual visibility.

## X-green reflection prototype and molten heat

`bake_facets.py` requires Pillow and derives a 320×320 RGBA8 numerical map from
`Assets/Textures/Cells/71.png`. Color-connected plateaus receive one of eight
inclined planes or a flat plane; B masks green mineral, rejecting blue matrix.
The bake is deterministic and validated against the committed bytes. It is an
automatic first pass, not an artist-approved normal map.

Only X-green samples this map (400 KiB, Point, no mipmaps). The visible Terrain
pass adds a reflection using the gradient of solved world irradiance; this is
an approximate directional cue, not a true ray direction. Flat light gives no
reflection. Rotated/mirrored UV bases transform the normals with the texture.
This reflection is deliberately absent from the material/emission pass to avoid
feeding solved light back into emission. X-green internal modulation is weak and
restricted to mineral color; other X-crystals retain their current effect.

Incremental cost: one normal-map read per X-green fragment and four light reads
for masked mineral fragments, plus tangent/normal and narrow specular math.
Derivative basis calculation has four float2 derivatives in the visible pass.
No extra draws, dispatches or per-frame allocations. Lava adds one existing
phase-map sample and one sincos plus polynomial heat math per lava fragment in
each terrain pass. Heat modulates authored veins; sheet addressing is unchanged.

`reflection.py` executes the actual HLSL with an analytic irradiance fixture,
checking directional response, mirrored basis, matrix rejection, uniform-light
rejection and hot/cold lava contrast. These are CPU math checks only. Unity
compilation, actual texture binding, visual quality and GPU frame time remain
unverified; inspect an X-green patch while moving a light and a multi-cell lava
patch before judging this prototype.
