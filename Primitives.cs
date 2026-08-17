using System;

namespace Voxon
{
    // =====================================================================
    //  Reusable voxel PRIMITIVES for the VLED template - disc (ground), cube,
    //  sphere, cone, cylinder.  Each is rasterised as a hollow SHELL (surface
    //  voxels only - solids wash out on the display) and every emitted voxel
    //  carries an analytic surface NORMAL for the lighting pass.
    //
    //  These build into MODEL space (pass cs=1, sn=0); the per-frame spin is
    //  applied on the GPU as a transform.  `cs`/`sn` are kept as parameters so
    //  a primitive can still be pre-rotated in model space if wanted.
    //
    //  Sampling pitch `ds` is the DPI spacing from GameModel (tied to the
    //  volume) - never derive it from the primitive's size.
    // =====================================================================
    internal static class Primitives
    {
        public delegate bool InsideFn(float x, float y, float z);
        public delegate void NormalFn(float x, float y, float z, out float nx, out float ny, out float nz);

        // Generic rasteriser: walk a local grid, keep surface voxels (or all,
        // if solid), then place them with their (spun) normal.
        static void Shell(VoxBatch v, float cx, float cy, float cz, float ext, float ds,
                          int col, float cs, float sn, InsideFn inside, NormalFn normal, bool solid)
        {
            for (float x = -ext; x <= ext; x += ds)
                for (float y = -ext; y <= ext; y += ds)
                    for (float z = -ext; z <= ext; z += ds)
                    {
                        if (!inside(x, y, z)) continue;
                        if (!solid &&
                            inside(x + ds, y, z) && inside(x - ds, y, z) &&
                            inside(x, y + ds, z) && inside(x, y - ds, z) &&
                            inside(x, y, z + ds) && inside(x, y, z - ds))
                            continue;   // fully-interior voxel -> skip (shell only)

                        normal(x, y, z, out float nx, out float ny, out float nz);
                        float wx = cx + x, wy = cy + y, wz = cz + z;
                        float rx = wx * cs - wy * sn;
                        float ry = wx * sn + wy * cs;
                        float rnx = nx * cs - ny * sn;
                        float rny = nx * sn + ny * cs;
                        v.Add(rx, ry, wz, rnx, rny, nz, col);
                    }
        }

        // ---- flat disc (single-voxel-thick ground plane) ----------------
        public static void Disc(VoxBatch v, float cx, float cy, float cz, float r,
                                float ds, int col, float cs, float sn)
        {
            float r2 = r * r;
            for (float x = -r; x <= r; x += ds)
                for (float y = -r; y <= r; y += ds)
                {
                    if (x * x + y * y > r2) continue;
                    float wx = cx + x, wy = cy + y;
                    v.Add(wx * cs - wy * sn, wx * sn + wy * cs, cz, 0f, 0f, -1f, col);   // normal up (-Z)
                }
        }

        // ---- sphere -----------------------------------------------------
        public static void Sphere(VoxBatch v, float cx, float cy, float cz, float r,
                                  float ds, int col, float cs, float sn, bool solid = false)
        {
            float r2 = r * r;
            Shell(v, cx, cy, cz, r, ds, col, cs, sn,
                (x, y, z) => x * x + y * y + z * z <= r2,
                (float x, float y, float z, out float nx, out float ny, out float nz) =>
                {
                    float l = MathF.Sqrt(x * x + y * y + z * z);
                    if (l < 1e-5f) { nx = 0; ny = 0; nz = 1; return; }
                    nx = x / l; ny = y / l; nz = z / l;
                }, solid);
        }

        // ---- sphere, analytic surface (for DYNAMIC scenes) --------------
        // Sphere() rasterises a local grid, so it costs O((2r/ds)^3) - fine for
        // a static model built once, far too slow for spheres rebuilt every
        // frame.  This walks the surface directly instead: latitude rings with
        // a circumference-derived longitude step, so the cost is O(4*pi*r^2/ds^2)
        // and the normal is exactly radial.
        public static void SphereSurface(VoxBatch v, float cx, float cy, float cz,
                                         float r, float ds, int col)
        {
            if (r <= 0f || ds <= 0f) return;
            int lat = Math.Max(2, (int)MathF.Ceiling(MathF.PI * r / ds));
            for (int i = 0; i <= lat; i++)
            {
                float theta = MathF.PI * i / lat;                  // 0..pi from +Z
                float sr = MathF.Sin(theta), sz = MathF.Cos(theta);
                int lon = Math.Max(1, (int)MathF.Ceiling(2f * MathF.PI * r * sr / ds));
                for (int j = 0; j < lon; j++)
                {
                    float phi = 2f * MathF.PI * j / lon;
                    float nx = sr * MathF.Cos(phi), ny = sr * MathF.Sin(phi);
                    v.Add(cx + r * nx, cy + r * ny, cz + r * sz, nx, ny, sz, col);
                }
            }
        }

        // ---- axis-aligned cube -----------------------------------------
        public static void Cube(VoxBatch v, float cx, float cy, float cz, float half,
                                float ds, int col, float cs, float sn, bool solid = false)
        {
            float t = half - ds * 1.5f;
            Shell(v, cx, cy, cz, half, ds, col, cs, sn,
                (x, y, z) => MathF.Abs(x) <= half && MathF.Abs(y) <= half && MathF.Abs(z) <= half,
                (float x, float y, float z, out float nx, out float ny, out float nz) =>
                {
                    nx = MathF.Abs(x) >= t ? MathF.Sign(x) : 0f;
                    ny = MathF.Abs(y) >= t ? MathF.Sign(y) : 0f;
                    nz = MathF.Abs(z) >= t ? MathF.Sign(z) : 0f;
                    float l = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                    if (l < 1e-5f)
                    {
                        float ax = MathF.Abs(x), ay = MathF.Abs(y), az = MathF.Abs(z);
                        if (ax >= ay && ax >= az) nx = MathF.Sign(x);
                        else if (ay >= az) ny = MathF.Sign(y);
                        else nz = MathF.Sign(z);
                        return;
                    }
                    nx /= l; ny /= l; nz /= l;
                }, solid);
        }

        // ---- cylinder (axis = Z) ---------------------------------------
        public static void Cylinder(VoxBatch v, float cx, float cy, float cz, float r, float halfH,
                                    float ds, int col, float cs, float sn, bool solid = false)
        {
            float r2 = r * r, ext = MathF.Max(r, halfH), capT = halfH - ds * 1.5f;
            Shell(v, cx, cy, cz, ext, ds, col, cs, sn,
                (x, y, z) => x * x + y * y <= r2 && MathF.Abs(z) <= halfH,
                (float x, float y, float z, out float nx, out float ny, out float nz) =>
                {
                    if (MathF.Abs(z) >= capT) { nx = 0; ny = 0; nz = MathF.Sign(z); return; }
                    float l = MathF.Sqrt(x * x + y * y);
                    if (l < 1e-5f) { nx = 0; ny = 0; nz = MathF.Sign(z); return; }
                    nx = x / l; ny = y / l; nz = 0;
                }, solid);
        }

        // ---- cone (apex toward -Z = "up"; base at +Z) ------------------
        public static void Cone(VoxBatch v, float cx, float cy, float cz, float baseR, float halfH,
                                float ds, int col, float cs, float sn, bool solid = false)
        {
            float ext = MathF.Max(baseR, halfH);
            float slope = baseR / (2f * halfH);
            float nl = MathF.Sqrt(1f + slope * slope);
            float baseT = halfH - ds * 1.5f;
            Shell(v, cx, cy, cz, ext, ds, col, cs, sn,
                (x, y, z) =>
                {
                    if (MathF.Abs(z) > halfH) return false;
                    float rz = baseR * (z + halfH) / (2f * halfH);
                    return x * x + y * y <= rz * rz;
                },
                (float x, float y, float z, out float nx, out float ny, out float nz) =>
                {
                    if (z >= baseT) { nx = 0; ny = 0; nz = 1; return; }
                    float l = MathF.Sqrt(x * x + y * y);
                    if (l < 1e-5f) { nx = 0; ny = 0; nz = -1; return; }
                    nx = (x / l) / nl; ny = (y / l) / nl; nz = -slope / nl;
                }, solid);
        }
    }
}
