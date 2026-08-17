using System;

namespace Voxon
{
    // =====================================================================
    //  HELLO VOXEL  -  the "game" model for the VLED template.
    //
    //  This is the ONLY file you rewrite per app.  Everything else
    //  (Program.cs UI, GameHost.cs loop + Bridge, Lighting.cs, GpuLighting.cs,
    //  Audio.cs, Persist.cs and the SDK wrappers) is reusable boilerplate.
    //
    //  ARCHITECTURE (matches VLEDStudio, the reference renderer):
    //   * The scene is built ONCE as a STATIC MODEL in model space (no spin),
    //     into Model.  It's only rebuilt when something geometric changes
    //     (density, volume, title) - see EnsureModel().
    //   * Every frame the GPU (GpuLighting) applies the spin as a transform +
    //     lights the model and writes the lit, world-space result into
    //     OutX/OutY/OutZ/OutC, which GameHost draws in one DrawVox_Batch.
    //   * Uploading the model once (not every frame) is what lets it push ~1M
    //     voxels with spotlights + shadows in real time.
    //
    //  Universal rules kept: everything is voxels; solids are hollow SHELLS
    //  (never filled - they wash out on the display); voxel pitch is a DPI
    //  setting tied to the volume; geometry is expressed relative to BOUNDR/Z.
    // =====================================================================

    internal enum Dir6 { AngCW, AngCCW, RadOut, RadIn, VertUp, VertDown, None }
    internal enum GameState { Ready, Playing, Paused, GameOver }

    // A growable Structure-of-Arrays voxel set (model space for Model).
    internal sealed class VoxBatch
    {
        public float[] X, Y, Z;      // position
        public float[] NX, NY, NZ;   // surface normal (unit)
        public int[] C;              // albedo colour 0xRRGGBB
        public int Count;
        public VoxBatch(int cap)
        {
            X = new float[cap]; Y = new float[cap]; Z = new float[cap];
            NX = new float[cap]; NY = new float[cap]; NZ = new float[cap];
            C = new int[cap];
        }
        public void Clear() => Count = 0;

        public void Add(float x, float y, float z, float nx, float ny, float nz, int col)
        {
            if (Count >= X.Length) return;
            int i = Count;
            X[i] = x; Y[i] = y; Z[i] = z;
            NX[i] = nx; NY[i] = ny; NZ[i] = nz;
            C[i] = col; Count++;
        }
    }

    internal static class GameModel
    {
        // ---- volume bounds (device units; set by GameHost) ----------------
        //   VX2 : r = 2, z = 2      VX2-XL : r = 4, z = 2      (-Z is up)
        public static float BOUNDR = 2.0f;
        public static float BOUNDZ = 2.0f;
        public static void SetBounds(float r, float z)
        {
            float nr = r > 0.05f ? r : 2f, nz = z > 0.05f ? z : 2f;
            if (nr != BOUNDR || nz != BOUNDZ) { BOUNDR = nr; BOUNDZ = nz; _modelDirty = true; }
        }

        // ---- universal visual controls (geometry -> rebuild on change) ----
        static float _density = 2.0f, _figDen = 3.0f, _textSize = 1.0f;
        static int _color = 0x20E0C0;
        static bool _showTitle;
        public static float Density { get => _density; set { if (value != _density) { _density = value; _modelDirty = true; } } }
        public static float FigureDensity { get => _figDen; set { if (value != _figDen) { _figDen = value; _modelDirty = true; } } }
        public static int Color { get => _color; set { if (value != _color) { _color = value; _modelDirty = true; } } }
        public static float TextSize { get => _textSize; set { if (value != _textSize) { _textSize = value; _modelDirty = true; } } }
        public static bool ShowTitle { get => _showTitle; set { if (value != _showTitle) { _showTitle = value; _modelDirty = true; } } }
        public static bool DemoMode = false;

        // ---- placeholder GAME controls -----------------------------------
        public static float RotSpeed = 0.5f;      // spin rate (turns/sec-ish)

        // ---- placeholder game state --------------------------------------
        public static int Score, HiScore, Level = 1, Lives = 3;
        public static GameState State = GameState.Playing;
        public static int VoxelCount => Model.Count;
        public static float Clock => _time;
        public static float Spin => _spin;
        public static string Banner => State == GameState.Paused ? "PAUSED" : "";
        public static string Hint => "HELLO VOXEL - VLED template  |  edit GameModel.cs to build your game";

        // Voxel pitch as an absolute distance tied to the volume (DPI).
        static float Spacing => BOUNDR * 0.02f / Math.Clamp(Density, 0.4f, 8f);

        // ---- static model + lit output -----------------------------------
        private const int VOX_CAPACITY = 2_200_000;
        public static readonly VoxBatch Model = new VoxBatch(VOX_CAPACITY);   // model space, static
        public static float[] OutX = new float[VOX_CAPACITY];                 // world space, lit (per frame)
        public static float[] OutY = new float[VOX_CAPACITY];
        public static float[] OutZ = new float[VOX_CAPACITY];
        public static int[]   OutC = new int[VOX_CAPACITY];
        public static int OutCount;

        private static bool _modelDirty = true;
        private static float _spin, _time;
        private static int _wantNudge;

        // =================================================================
        //  Lifecycle
        // =================================================================
        public static void Init() { _spin = 0f; _time = 0f; State = GameState.Playing; _modelDirty = true; }
        public static void RequestNewGame() { _spin = 0f; State = GameState.Playing; }
        public static void Randomize() { _spin = 0f; }
        public static void Regenerate(int a, int b, int c) { }
        public static void TogglePause()
            => State = State == GameState.Paused ? GameState.Playing : GameState.Paused;

        public static void SetWant(Dir6 d)
        {
            switch (d)
            {
                case Dir6.AngCW: case Dir6.RadOut: case Dir6.VertUp: _wantNudge = +1; break;
                case Dir6.AngCCW: case Dir6.RadIn: case Dir6.VertDown: _wantNudge = -1; break;
            }
        }
        public static void SetWantWorld(float nx, float ny, float nz)
            => _wantNudge = nx >= 0f ? +1 : -1;

        // =================================================================
        //  Simulation - advance the spin (applied on the GPU as a transform)
        // =================================================================
        public static void Tick(float dt)
        {
            if (State == GameState.Paused) return;
            _time += dt;
            float speed = RotSpeed;
            if (_wantNudge != 0) { speed += _wantNudge * 1.5f; _wantNudge = 0; }
            _spin += dt * speed * MathF.Tau * 0.25f;
        }

        // =================================================================
        //  Static model - built once (and on geometry change), spin = 0.
        //  The lighting/transform is applied per frame on the GPU.
        // =================================================================
        public static void EnsureModel()
        {
            if (!_modelDirty) return;
            Model.Clear();
            if (ShowTitle) BuildTitleModel(); else BuildPrimitiveModel();
            _modelDirty = false;
            GpuLighting.MarkMeshDirty();   // upload the new model once
        }

        // The lighting-test scene, in model space: cube / sphere / cone /
        // cylinder resting on a ground plane, all hollow SHELLS.  The sphere
        // hides a nested core so the shell-cull in Spotlight mode has interior
        // geometry to hide (the core vanishes under spotlights, shows in Flat).
        static void BuildPrimitiveModel()
        {
            float ds = Spacing;
            float ring = BOUNDR * 0.55f;
            float s = BOUNDR * 0.26f;
            const float cs = 1f, sn = 0f;   // model space (no spin)

            float floorTop = BOUNDZ * 0.66f;
            const int groundCol = 0x707486;
            Primitives.Disc(Model, 0, 0, floorTop, BOUNDR * 0.85f, ds, groundCol, cs, sn);

            float ObjZ(float half) => floorTop - half;
            int sphere = Color;
            const int orange = 0xE08040, green = 0x60E040, magenta = 0xE040C0, core = 0xFFF080;

            Primitives.Sphere(Model, ring, 0, ObjZ(s), s, ds, sphere, cs, sn);
            Primitives.Sphere(Model, ring, 0, ObjZ(s), s * 0.5f, ds, core, cs, sn);   // nested core
            Primitives.Cube(Model, 0, ring, ObjZ(s * 0.9f), s * 0.9f, ds, orange, cs, sn);
            Primitives.Cone(Model, -ring, 0, ObjZ(s), s, s, ds, green, cs, sn);
            Primitives.Cylinder(Model, 0, -ring, ObjZ(s), s * 0.7f, s, ds, magenta, cs, sn);
        }

        // Classic scene: the words "HELLO" / "VOXEL" as a voxel slab, model
        // space.  Radial-outward normals so spotlights catch the facing side.
        static void BuildTitleModel()
        {
            const int cols = 5 * FONT_W + 4;
            const int rows = FONT_H * 2 + 3;
            float half = 0.90f;
            float sizeH = (BOUNDR * 2f * half) / cols;
            float sizeV = (BOUNDZ * 2f * half) / rows;
            float cell = MathF.Min(sizeH, sizeV) * Math.Clamp(TextSize, 0.5f, 1.4f);
            const int white = 0xF0F0FF;
            DrawWord("HELLO", 0, Color, cell, cols, rows);
            DrawWord("VOXEL", 1, white, cell, cols, rows);
        }

        static void DrawWord(string word, int line, int col, float cell, int cols, int rows)
        {
            float step = MathF.Max(Spacing, cell * 0.12f);
            float thick = cell * 0.40f;
            float u0 = -cols * 0.5f;
            float lineTopRow = line * (FONT_H + 3);
            float w0 = rows * 0.5f - lineTopRow;

            for (int ci = 0; ci < word.Length; ci++)
            {
                byte[] g = Glyph(word[ci]);
                float cu = u0 + ci * (FONT_W + 1);
                for (int ry = 0; ry < FONT_H; ry++)
                    for (int rx = 0; rx < FONT_W; rx++)
                    {
                        if ((g[ry] & (1 << (FONT_W - 1 - rx))) == 0) continue;
                        float pu = (cu + rx + 0.5f) * cell;
                        float pw = (w0 - ry - 0.5f) * cell;
                        FillCell(pu, pw, cell, step, thick, col);
                    }
            }
        }

        static void FillCell(float pu, float pw, float cell, float step, float thick, int col)
        {
            float h = cell * 0.5f;
            for (float du = -h; du <= h; du += step)
                for (float dw = -h; dw <= h; dw += step)
                    for (float dt = -thick * 0.5f; dt <= thick * 0.5f; dt += step)
                    {
                        float x = pu + du;      // model space (spin applied on GPU)
                        float y = dt;
                        float z = -(pw + dw);
                        float l = MathF.Sqrt(x * x + y * y);
                        float nx = l > 1e-4f ? x / l : 0f, ny = l > 1e-4f ? y / l : 1f;
                        Model.Add(x, y, z, nx, ny, 0f, col);   // radial-out normal
                    }
        }

        // =================================================================
        //  A tiny 5x7 bitmap font (only HELLO VOXEL letters have glyphs).
        // =================================================================
        private const int FONT_W = 5, FONT_H = 7;
        static byte[] Glyph(char c) => c switch
        {
            'H' => new byte[] { 0b10001, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001 },
            'E' => new byte[] { 0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b11111 },
            'L' => new byte[] { 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b11111 },
            'O' => new byte[] { 0b01110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110 },
            'V' => new byte[] { 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01010, 0b00100 },
            'X' => new byte[] { 0b10001, 0b10001, 0b01010, 0b00100, 0b01010, 0b10001, 0b10001 },
            _   => new byte[FONT_H],
        };

        public static int HsvToRgb(float h, float s, float v)
        {
            h = ((h % 360f) + 360f) % 360f;
            float c = v * s, x = c * (1 - MathF.Abs((h / 60f) % 2 - 1)), m = v - c;
            float r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; }
            else if (h < 120) { r = x; g = c; }
            else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; }
            else if (h < 300) { r = x; b = c; }
            else { r = c; b = x; }
            return ((int)((r + m) * 255) << 16) | ((int)((g + m) * 255) << 8) | (int)((b + m) * 255);
        }
    }
}
