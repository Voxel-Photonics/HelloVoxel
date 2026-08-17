# HelloVoxel — Lighting Guide

How the lighting works, what each mode does, and how to drive it from code.
The lighting system is cloned from **VLEDStudio** (the reference renderer): the
scene is built **once** as a static model and the **GPU** applies the per-frame
transform + lighting, so even ~1M voxels run in real time.

---

## 1. The big idea (why it's fast)

Most naive voxel apps rebuild and re-upload every voxel every frame. That upload
(~1.4 ms per GPU copy) is the wall at high voxel counts.

HelloVoxel instead:

1. Builds the scene **once**, in **model space** (no rotation), into a static
   buffer — `GameModel.Model`. It's only rebuilt when geometry actually changes.
2. Each frame, passes a small **transform matrix** (the spin) to the GPU. The
   GPU rotates the points + normals, lights them, and writes the finished
   world-space result into `GameModel.Out*`.
3. `GameHost` draws `Out*` in one `DrawVox_Batch`.

So the per-frame GPU→CPU traffic is one upload of *lights* and one *readback* of
colours — the model itself is uploaded only when it changes.

```
Tick ─▶ Lighting.UpdateLights(time) ─▶ Lighting.RenderFrame()
                                          │
                                          ├─ GameModel.EnsureModel()   (rebuild only if dirty)
                                          ├─ build spin transform
                                          ├─ PackLightData(...)        (lights → 24-float records)
                                          └─ GpuLighting.RunPointLights(...) ─▶ GameModel.Out{X,Y,Z,C}, OutCount
                                                (or CPU fallback)
GameHost ─▶ DrawVox_Batch(OutX, OutY, OutZ, OutC, OutCount)
```

Files: [Lighting.cs](Lighting.cs) (coordinator + modes + light packing),
[GpuLighting.cs](GpuLighting.cs) (DX12 compute shaders), [GameModel.cs](GameModel.cs)
(the static model), [Primitives.cs](Primitives.cs) (shape helpers).

---

## 2. The three lighting modes

Set via `Lighting.Mode` (or the **Lighting** tab / menu). Values:
`LightMode.Flat = 0`, `LightMode.Normals = 1`, `LightMode.Spotlight = 2`.

### Flat  (`LightMode.Flat`)
Positions are transformed to world space; **colours pass straight through**, no
shading. Fastest; a "no-shading" view. Runs on the CPU (`Parallel.For`) — it's
already trivial.

### Normals  (`LightMode.Normals`)
Brightness comes from each voxel's **world normal · light direction**
(half-Lambert), against one imaginary light. **No shadows.** Good for reading
surface shape. CPU path. Knobs:
- `Lighting.NormalIntensity` — overall multiplier.
- `Lighting.NormalStrength` — 0 = flat, 1 = full N·L.
- `Lighting.LightAngleDeg` — azimuth of the imaginary light.
- `Lighting.Ambient` — floor added on top.

### Spotlight  (`LightMode.Spotlight`)  — the GPU path
Coloured **point lights** with inverse-square falloff, each casting a real
**shadow** (per-light orthographic shadow map), plus **6-face shell-visibility
culling** so interior geometry is hidden (e.g. the sphere's nested core vanishes
under the lights but shows in Flat). This is the mode that does "1M voxels + two
spotlights + shadows". Knobs:
- `Lighting.SpotIntensity` — main light intensity (0..10).
- `Lighting.SpotRadius` — falloff distance (atten = 0.5 at this distance).
- `Lighting.Ambient` — ambient floor (0 = only lit surfaces visible).
- `Lighting.Brightness` — overall exposure.
- `Lighting.ShadowBias` — world-space self-shadow bias. Raise if shadows look
  noisy/speckly, lower if they detach from contact points.
- `Lighting.OrbitLights` — sweep the key light so shadows move.

Shadows are **hard** (`SFac = 0` in the shader): a shadowed surface receives
zero light from that spotlight, which is what keeps interior geometry hidden.
If there's no DX12 GPU (or `Lighting.UseGpu == false`), Spotlight falls back to
a CPU implementation that produces the **same** result (shadows + shell cull),
just slower — so GPU-on and GPU-off look identical. The HUD shows `GPU` / `CPU`
under the VPS count.

---

## 3. Driving it programmatically

### 3.1 Set the mode + parameters

Everything is plain static fields — set them any time (the render thread reads
them each frame). In this template the UI writes them via `Bridge` →
`GameHost.ProcessBridge`, but you can set them directly:

```csharp
Lighting.Mode          = LightMode.Spotlight;
Lighting.Ambient       = 0.15f;   // ambient floor
Lighting.Brightness    = 1.0f;    // exposure
Lighting.SpotIntensity = 8.0f;    // 0..10
Lighting.SpotRadius    = 4.0f;    // falloff distance
Lighting.ShadowBias    = 0.02f;   // raise if shadows are noisy
Lighting.OrbitLights   = true;
Lighting.UseGpu        = true;    // false forces the CPU path
```

### 3.2 Build your own scene (the important part)

Fill `GameModel.Model` in **model space** (no spin — the GPU applies it), give
every voxel a **unit surface normal**, then mark the model dirty so it re-uploads
once. Do this in `GameModel.EnsureModel()` (rebuild-when-dirty) — see
`BuildPrimitiveModel()` for the working example.

```csharp
var m = GameModel.Model;
m.Clear();

// one voxel: position, normal (unit), colour 0xRRGGBB
m.Add(x, y, z,  nx, ny, nz,  0x20E0C0);

// ... add the rest of your surface voxels ...

GpuLighting.MarkMeshDirty();   // upload the new model once
```

Rules that keep it looking right on the display:
- **Shells, never solids.** Emit surface voxels only — a filled volume washes out
  (the display is additive) and wastes the voxel budget. `Primitives.cs` does
  shell rasterisation for you.
- **Normals matter.** Spotlight/Normals shading is only as good as your normals.
  Analytic normals (radial for a sphere, face normal for a cube) look best.
- **Fit to the volume.** Positions live in `[-BOUNDR,BOUNDR]` (XY) and
  `[-BOUNDZ,BOUNDZ]` (Z). `-Z` is up. Keep the dead centre clear.
- **Rebuild only on change.** Anything that alters geometry (density, colours,
  volume size) should set a dirty flag and call `GpuLighting.MarkMeshDirty()`
  (or `MarkColorsDirty()` for a colour-only change). The spin is *not* a rebuild
  — it's a per-frame transform.

### 3.3 Ready-made shape helpers

`Primitives` build hollow shells with correct normals into a `VoxBatch`
(model space; pass `cs = 1, sn = 0` for no pre-rotation, `ds` = voxel pitch):

```csharp
float ds = /* voxel pitch, e.g. BOUNDR * 0.01f */;
Primitives.Sphere  (m, cx, cy, cz, r,            ds, col, 1f, 0f);
Primitives.Cube    (m, cx, cy, cz, half,         ds, col, 1f, 0f);
Primitives.Cylinder(m, cx, cy, cz, r, halfH,     ds, col, 1f, 0f);
Primitives.Cone    (m, cx, cy, cz, baseR, halfH, ds, col, 1f, 0f);
Primitives.Disc    (m, cx, cy, cz, r,            ds, col, 1f, 0f);   // flat ground
```

### 3.4 Per-frame render (already wired in GameHost)

```csharp
GameModel.Tick(dt);              // advances the spin angle
Lighting.UpdateLights(time);     // positions the lights (orbit)
Lighting.RenderFrame();          // EnsureModel + transform + light -> GameModel.Out*
host.DrawVox_Batch(ref vs,
    ref GameModel.OutX[0], ref GameModel.OutY[0], ref GameModel.OutZ[0],
    ref GameModel.OutC[0], GameModel.OutCount, 0);
```

---

## 4. Lights

Two spotlights by default (a white orbiting key + a cool fill), set up in
`Lighting.UpdateLights(time)`. Each is a world-space source aimed at the origin.
To change the rig, edit `_src[...]` there (position, colour, intensity, radius,
target). `PackLightData()` turns each source into the 24-float record the GPU
needs (position, colour, intensity, radius, direction, right/up basis, and the
display-volume shadow projection).

If you want more than two lights, raise `LightCount` (cap `MaxLights = 8`) and
add entries in `UpdateLights`.

### Cost of more spotlights

Per **enabled** light, per frame:
- **+1 shadow-map fill dispatch** — O(N voxels): projects every voxel + one
  atomic-min into that light's shadow slice. The main per-light cost.
- **+1 shade-loop iteration** per voxel — a dot product, inverse-square
  attenuation, and one shadow-map gather. So shading is O(N × lights).
- **+`ShadowRes² × 4` bytes** shadow memory (4 MB at 1024²; all 8 slots are
  pre-allocated = 32 MB regardless).

What does **not** scale with light count (done once per frame): the transform
pass, the 6-face shell build, the model upload, the readback, and the map
clears. **Disabled lights (intensity 0) skip their shadow fill** — you only pay
for enabled ones.

Net: cost is **sub-linear** — going 2→4 lights doubles the shadow fills and the
shade inner loop, but the shared passes don't change, so real frame time rises
~30–50% for doubling lights, not 100%. Hard cap is **8**.

---

## 5. The GPU pipeline (reference)

`GpuLighting.RunPointLights(...)` runs four DX12 compute shaders per frame:

1. **TransformAndNormalShader** — model→world positions + rotate normals (this is
   where the spin is applied; the mesh buffer itself stays cached).
2. **ShellDepthFillShader** — 6-face orthographic min-depth (interior culling).
3. **PtLightDepthFillShader** — atomic-min shadow map, one dispatch per light.
4. **PointLightShader** — shell cull + per-light diffuse/attenuation/shadow.

Constants (in `Lighting.cs` / `GpuLighting.cs`): `ShadowRes = 1024`,
`ShellRes = 512`, `MaxLights = 8`. The model is uploaded only when
`MarkMeshDirty()` was called (or the voxel count changed); lights upload only
when they change; one readback per frame.

Packed light record — 24 floats/light:

| Index | Field |
|-------|-------|
| 0–2   | position XYZ |
| 3–5   | colour RGB (0..1) |
| 6     | intensity (0 = disabled) |
| 7     | falloff radius |
| 8–10  | direction (source→target, unit) |
| 11–13 | right basis |
| 14–16 | up basis |
| 17–18 | shadow minU, minV |
| 19–20 | shadow uScale, vScale |
| 21    | shadow minD |
| 22    | depth→int factor |
| 23    | shadow bias (int units, as float) |

---

## 6. Recording to a depth-map video (Ctrl+R)

Press **Ctrl+R**, or pick **Game → Record Depth Video** (the menu entry shows the
`Ctrl+R` shortcut and ticks while recording), to start/stop recording the live
volume to a **VoxelStudio depth-map video** — any demo built with this blueprint
can be captured and replayed in VoxelStudio. The HUD shows `● REC` under the VPS
count while active.

- **Where it writes:** `C:\VLED\Media\DepthVideo` if that folder exists;
  otherwise you're asked to pick a folder. Files are named after the **app** plus
  the date/time — `<AppName>_yyyy-MM-dd_HH-mm-ss.mkv`, e.g.
  `HelloVoxel_2026-07-29_14-30-12.mkv`. The name comes from the entry assembly,
  so a clone of this blueprint names its recordings after itself with no edit.
- **Format:** each frame's world-space voxels are encoded as the 6-face
  orthographic cubemap (colour band + 16-bit depth band, faces +X +Y −X −Y Top
  Bottom) and streamed as raw RGB24 to **ffmpeg**, muxed to lossless **FFV1 /
  MKV**. faceRes = 256 (VX2) or 512 (VX2-XL). See the `voxelstudio-depthmap`
  skill for the full format; encoder is [DepthRecorder.cs](DepthRecorder.cs)
  (cloned from VLEDStudio's `VoxelDepthMap`).
- **Requires ffmpeg** on `PATH` (or a WinGet install) — the same dependency
  VLEDStudio uses for baking.

Programmatically: `DepthRecorder.Start(boundr, boundz, dir)` /
`DepthRecorder.Capture(bx, by, bz, bc, count)` (called each frame by GameHost) /
`DepthRecorder.Stop()`.

## 7. Tuning cheatsheet

| Symptom | Fix |
|---------|-----|
| Shadows look noisy/speckly | raise `ShadowRes` (finer map) — bias is the wrong lever |
| Shadows detach from contacts | lower `Lighting.ShadowBias` |
| Everything too dark | raise `Lighting.Ambient` or `Brightness` |
| Interior geometry showing | you're in Flat/Normals — use Spotlight (shell cull) |
| Frame rate low on huge scenes | ensure the model is static (don't call `MarkMeshDirty` every frame); lower **Density** |
