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
