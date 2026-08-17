# HELLO VOXEL — VLED app blueprint

A minimal, buildable Voxon **VLED** (volumetric-display) app for **VX2 / VX2-XL**.
It opens an Avalonia window with the **LedWin emulator embedded**, drives the
physical volume, and renders a **lighting test bench** — cube / sphere / cone /
cylinder resting on a ground plane, slowly spinning — lit by one of three
**lighting modes** (Flat / Normals / Spotlight). Everything is a hollow shell
(solids wash out on a volumetric display); the sphere hides a nested core so the
Spotlight mode's shell-cull has interior geometry to hide, and the ground catches
the spotlight shadows. Tick *Show HELLO VOXEL text* for the classic words scene.

Use it as the starting point for any VLED game or app. The lighting system is a
clone of **VLEDStudio**, the reference renderer — see **[hellovoxel.md](hellovoxel.md)**
for the full lighting guide (modes, programmatic use, the GPU pipeline, tuning).

---

## Quick start

```bash
dotnet run --project HelloVoxel.csproj
```

It **builds with no hardware attached** (the Voxon DLLs load at run time);
*running* needs the display or its simulator (LedWin) on the machine. Windows +
a DX12 GPU are recommended — the lighting runs on the GPU (ComputeSharp/DX12),
with an automatic CPU fallback if there's no capable GPU.

> The `.exe` is file-locked while the app is running — close the window before
> you rebuild.

---

## What's boilerplate vs what you change

| File | Role | Change it? |
|------|------|-----------|
| `LedHostCS.cs`, `LedWinCS.cs`, `VoxonTypes.cs` | Voxon SDK wrappers | no |
| `app.manifest`, `HelloVoxel.csproj` | project + native manifest | no |
| `Program.cs` | Avalonia UI: menus, tabs (Game / Lighting / Hardware), embedded simulator, HUD | add your controls |
| `GameHost.cs` | `Bridge` + render/game loop, hardware, input, camera, lighting hook | extend `Bridge` only |
| `Lighting.cs` | per-frame render coordinator — modes, light packing, transform, CPU fallback | reusable |
| `GpuLighting.cs` | GPU (ComputeSharp/DX12) transform + shadow + point-light shade | reusable (cloned from VLEDStudio) |
| `Primitives.cs` | disc / cube / sphere / cone / cylinder shells with surface normals | reusable helpers |
| `Audio.cs` | procedural mixer + music/SFX | tweak tunes |
| `Persist.cs` | JSON settings + high scores | add your fields |
| **`GameModel.cs`** | **your app** — the static model, `Tick`, `EnsureModel` | **write this** |

The one file you rewrite per app is **`GameModel.cs`**. Everything else is
reusable plumbing.

---

## How the frame works

```
Tick(dt) ─▶ Lighting.UpdateLights(t) ─▶ Lighting.RenderFrame() ─▶ DrawVox_Batch(Out*)
             (move lights)               │
                                         ├─ GameModel.EnsureModel()  (rebuild only if dirty)
                                         ├─ build spin transform
                                         └─ light on GPU → GameModel.Out{X,Y,Z,C}, OutCount
```

The key performance idea (from VLEDStudio): the scene is built **once** as a
static model in **model space**; the **GPU** applies the per-frame rotation +
lighting. A static model — even ~1M voxels — never re-uploads, which is what lets
it run big scenes with spotlights + shadows in real time. See
[hellovoxel.md](hellovoxel.md) for the full pipeline.

---

## Lighting modes

Pick a mode on the **Lighting** tab (or the *Lighting* menu). Full details and
knobs are in [hellovoxel.md](hellovoxel.md); in short:

- **Flat** — positions transformed to world space, colours passed straight
  through. No shading. The "no-lighting" view.
- **Normals** — brightness from each voxel's world **normal · light direction**
  (half-Lambert), no shadows. Good for reading surface shape. Knobs: *Strength*,
  *Intensity*, *Angle*.
- **Spotlight** *(default)* — GPU coloured **point lights** with inverse-square
  falloff, per-light **shadow maps**, and 6-face **shell-visibility culling** so
  interior geometry is hidden (the sphere's nested core vanishes under lights,
  shows in Flat). Knobs: *Intensity*, *Radius*, *Shadow bias*, *Ambient*,
  *Exposure*, *Orbit*.

The HUD shows `GPU` / `CPU` under the VPS count. Spotlight's CPU fallback
produces the **same** result (shadows + shell cull), just slower.

**Set a mode / params from code:**

```csharp
Lighting.Mode          = LightMode.Spotlight;   // Flat | Normals | Spotlight
Lighting.Ambient       = 0.15f;                 // ambient floor
Lighting.Brightness    = 1.0f;                  // exposure
Lighting.SpotIntensity = 8.0f;                  // 0..10
Lighting.SpotRadius    = 4.0f;                  // falloff distance
Lighting.ShadowBias    = 0.02f;                 // raise if shadows look speckly
Lighting.OrbitLights   = true;
Lighting.UseGpu        = true;                  // false forces the CPU path
```

To change the light rig (positions, colours, count — up to 8), edit
`Lighting.UpdateLights`.

---

## Make it your own

1. **Write your scene** in `GameModel.EnsureModel()`. Fill `GameModel.Model` in
   **model space** (no spin — the GPU applies it), giving every voxel a **unit
   surface normal**, then mark it dirty so it uploads once:

   ```csharp
   var m = GameModel.Model;
   m.Clear();
   m.Add(x, y, z,  nx, ny, nz,  0x20E0C0);   // position, unit normal, 0xRRGGBB
   // ... add the rest of your surface voxels ...
   GpuLighting.MarkMeshDirty();               // upload the new model once
   ```

   Or use the shape helpers (they emit shells with correct normals):

   ```csharp
   float ds = /* voxel pitch */;
   Primitives.Sphere  (m, cx, cy, cz, r,            ds, col, 1f, 0f);
   Primitives.Cube    (m, cx, cy, cz, half,         ds, col, 1f, 0f);
   Primitives.Cylinder(m, cx, cy, cz, r, halfH,     ds, col, 1f, 0f);
   Primitives.Cone    (m, cx, cy, cz, baseR, halfH, ds, col, 1f, 0f);
   Primitives.Disc    (m, cx, cy, cz, r,            ds, col, 1f, 0f);
   ```

2. **Rebuild only on change.** Anything that alters geometry (density, colours,
   volume size) should flip the dirty flag → `GameModel.EnsureModel` rebuilds and
   calls `MarkMeshDirty`. The spin is **not** a rebuild — it's a per-frame GPU
   transform.

3. **Advance your simulation** in `GameModel.Tick(dt)` (here it just advances the
   spin; a real game moves entities, scores, etc.).

4. **Add a control:** add a field to `Bridge` (in `GameHost.cs`), a control in
   `Program.cs` that writes it, a line in `GameHost.ProcessBridge` that applies
   it, and a field in `Persist.cs` to save it.

5. **Map input:** keyboard / Xbox / SpaceMouse already arrive as generic move
   intents — re-map them for your app in `GameModel.SetWant`.

---

## The rules that make it look right (universal to every VLED app)

- **One `DrawVox_Batch` per frame** — everything is voxels; no line/sphere calls.
- **Voxel density is DPI** — pitch is tied to the volume size, not the scene.
- **Fit to `BOUNDR` / `BOUNDZ`** so content fills VX2 or VX2-XL exactly.
  Positions live in `[-BOUNDR,BOUNDR]` (XY), `[-BOUNDZ,BOUNDZ]` (Z); **`-Z` is up**.
- **Solids render as hollow shells** (see `Primitives.cs`) — never filled; a
  filled volume washes out on the additive display and wastes the voxel budget.
- **UI/text lives in this window, never in the volume** (it's viewed from all
  sides at once).
- **Keep the dead centre clear** — the rotation axis is a blind spot.

---

## Retained, reusable controls

- **Game tab:** placeholder controls (New / Pause / Randomize / Demo), demo
  Size & Spin, universal Density / Figures / Colour, camera (Rotate / Tilt /
  Zoom / Reset), audio (Sound / Volume), Save Settings.
- **Lighting tab:** mode select, GPU toggle, Ambient / Exposure, Spotlight
  Intensity / Radius / Shadow-bias / Orbit, Normals Strength / Intensity / Angle,
  Show HELLO VOXEL text.
- **Hardware tab:** VX2 ↔ VX2-XL system swap, Motor + RPM, Bilinear, Border,
  Ordered dither + threshold, Gamma.
- **Input:** keyboard, Xbox controller, 3Dconnexion SpaceMouse (all wired to
  generic move intents — re-map in `GameModel.SetWant`).
- **Depth-map recording:** **Ctrl+R**, or **Game → Record Depth Video** (shortcut
  shown in the menu), records the live volume to a VoxelStudio depth-map video
  (FFV1/MKV) — writes to `C:\VLED\Media\DepthVideo` if it exists, else prompts for
  a folder. Files are named `<AppName>_yyyy-MM-dd_HH-mm-ss.mkv`. Needs `ffmpeg`.
  See [hellovoxel.md](hellovoxel.md#6-recording-to-a-depth-map-video-ctrlr).
- **Persistence:** settings in `hellovoxel.json` next to the exe.

---

## Tuning cheatsheet

| Symptom | Fix |
|---------|-----|
| Shadows look noisy/speckly | raise `ShadowRes` in `Lighting.cs` (bias is the wrong lever) |
| Shadows detach from contacts | lower the **Shadow bias** slider |
| Everything too dark | raise **Ambient** or **Exposure** |
| Interior geometry showing | switch to **Spotlight** (it shell-culls); Flat/Normals don't |
| Frame rate low on huge scenes | keep the model static (don't `MarkMeshDirty` every frame); lower **Density** |
| `GPU` never shows in the HUD | no DX12 device — it's on the CPU fallback |
