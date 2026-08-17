using System;
using System.Threading.Tasks;

namespace Voxon
{
    // =====================================================================
    //  LIGHTING  -  the per-frame render coordinator, modelled on VLEDStudio.
    //
    //  It takes the STATIC model (GameModel.Model, model space) and, each
    //  frame, builds a spin transform, then produces the lit, world-space
    //  result in GameModel.Out*.  Three modes:
    //
    //    * Flat     - positions transformed, colours passed straight through.
    //    * Normals  - brightness from world-normal . light direction (CPU).
    //    * Spotlight- GpuLighting.RunPointLights: coloured point lights with
    //                 inverse-square falloff + orthographic shadow maps +
    //                 6-face shell-visibility culling (interior geometry
    //                 hidden).  This is the fast GPU path - the model is
    //                 uploaded once and the GPU does transform + lighting.
    //
    //  Spotlight falls back to Flat if there's no DX12 GPU.
    // =====================================================================

    internal enum LightMode { Flat, Normals, Spotlight }

    internal static class Lighting
    {
        // ---- controls (set from the Bridge each frame) -----------------
        public static bool  UseGpu = true;
        public static bool  GpuActive;
        public static LightMode Mode = LightMode.Spotlight;
        public static float Ambient = 0.15f;      // ambient floor (0 = only spotlights)
        public static float Brightness = 1.0f;    // overall exposure
        public static float SpotIntensity = 8.0f; // main-light intensity (0..10)
        public static float SpotRadius = 4.0f;    // falloff radius (0.5..4)
        public static bool  OrbitLights = true;
        public static float NormalStrength = 1.0f;
        public static float NormalIntensity = 1.0f;
        public static float LightAngleDeg = 45f;

        public static float ShadowBias = 0.02f;    // world-space self-shadow bias (VLEDStudio default)
        const int ShadowRes = 1024;                 // finer map -> less shadow-map speckle (fill cost is per-voxel, not per-texel)
        const int ShellRes = 512;
        const float CullThresholdTexels = 4.0f;
        const int MaxLights = 8;
        const int LightCount = 2;          // two spotlights (main + fill)

        // CPU spotlight scratch (mirrors the GPU buffers) - grow-only
        static float[] _wnx = Array.Empty<float>(), _wny = Array.Empty<float>(), _wnz = Array.Empty<float>();
        static int[] _cpuShadow = Array.Empty<int>();
        static int[] _cpuShell = Array.Empty<int>();

        // world-space light sources (pos, colour, intensity, radius, target)
        struct Src { public float PX, PY, PZ, R, G, B, I, Rad, TX, TY, TZ; }
        static readonly Src[] _src = new Src[LightCount];
        static readonly float[] _packed = new float[MaxLights * 24];

        // Position the lights (main light orbits so shadows sweep).
        public static void UpdateLights(float time)
        {
            float br = GameModel.BOUNDR, bz = GameModel.BOUNDZ;
            float az = OrbitLights ? time * 0.6f : 0.9f;
            // main key light: white, orbiting, above the volume
            _src[0] = new Src {
                PX = 1.4f * br * MathF.Cos(az), PY = 1.4f * br * MathF.Sin(az), PZ = -1.1f * bz,
                R = 1f, G = 1f, B = 1f, I = SpotIntensity, Rad = SpotRadius,
                TX = 0, TY = 0, TZ = 0 };
            // fill light: cool, opposite side, dimmer
            _src[1] = new Src {
                PX = -1.5f * br, PY = 1.0f * br, PZ = -0.4f * bz,
                R = 0.5f, G = 0.65f, B = 1f, I = SpotIntensity * 0.35f, Rad = SpotRadius,
                TX = 0, TY = 0, TZ = 0 };
        }

        // =================================================================
        //  Per-frame entry point.  Fills GameModel.Out* + OutCount.
        // =================================================================
        public static void RenderFrame()
        {
            GameModel.EnsureModel();
            var m = GameModel.Model;
            int n = m.Count;
            GameModel.OutCount = n;
            if (n == 0) { GpuActive = false; return; }

            // spin transform (rotation about Z; scale 1; no translation)
            float a = GameModel.Spin;
            float cs = MathF.Cos(a), sn = MathF.Sin(a);
            // position rows
            float r0 = cs, r1 = -sn, r2 = 0f;
            float d0 = sn, d1 = cs, d2 = 0f;
            float f0 = 0f, f1 = 0f, f2 = 1f;
            // pure-rotation rows (normals) - same as position rows here
            float nr0 = cs, nr1 = -sn, nr2 = 0f;
            float nd0 = sn, nd1 = cs, nd2 = 0f;
            float nf0 = 0f, nf1 = 0f, nf2 = 1f;

            if (Mode == LightMode.Spotlight)
            {
                float br = GameModel.BOUNDR, bz = GameModel.BOUNDZ;
                PackLightData(LightCount, ShadowRes, br, bz);
                bool ok = UseGpu && GpuLighting.RunPointLights(
                    m.X, m.Y, m.Z, m.NX, m.NY, m.NZ, m.C, n,
                    r0, r1, r2, d0, d1, d2, f0, f1, f2,
                    0f, 0f, 0f,
                    nr0, nr1, nr2, nd0, nd1, nd2, nf0, nf1, nf2,
                    _packed, LightCount, ShadowRes,
                    Ambient, Brightness,
                    GameModel.OutX, GameModel.OutY, GameModel.OutZ, GameModel.OutC,
                    br, bz);
                GpuActive = ok;
                if (!ok) CpuSpotlight(m, n, r0, r1, r2, d0, d1, d2,
                                      nr0, nr1, nr2, nd0, nd1, nd2, nf0, nf1, nf2, br, bz);
                return;
            }

            GpuActive = false;
            if (Mode == LightMode.Normals)
                CpuNormals(m, n, r0, r1, r2, d0, d1, d2, nr0, nr1, nr2, nd0, nd1, nd2, nf0, nf1, nf2);
            else
                CpuFlat(m, n, r0, r1, r2, d0, d1, d2);
        }

        // ---- CPU: flat (transform + colours through) --------------------
        static void CpuFlat(VoxBatch m, int n, float r0, float r1, float r2, float d0, float d1, float d2)
        {
            float[] ox = GameModel.OutX, oy = GameModel.OutY, oz = GameModel.OutZ;
            int[] oc = GameModel.OutC;
            Parallel.For(0, n, i =>
            {
                float x = m.X[i], y = m.Y[i], z = m.Z[i];
                ox[i] = r0 * x + r1 * y + r2 * z;
                oy[i] = d0 * x + d1 * y + d2 * z;
                oz[i] = z;
                oc[i] = m.C[i];
            });
        }

        // ---- CPU: normal-based diffuse (no shadows) --------------------
        static void CpuNormals(VoxBatch m, int n,
            float r0, float r1, float r2, float d0, float d1, float d2,
            float nr0, float nr1, float nr2, float nd0, float nd1, float nd2, float nf0, float nf1, float nf2)
        {
            float az = LightAngleDeg * MathF.PI / 180f;
            float ce = MathF.Cos(0.7f), se = MathF.Sin(0.7f);
            float lx = ce * MathF.Cos(az), ly = ce * MathF.Sin(az), lz = -se;   // toward light
            float amb = Ambient, ni = NormalIntensity, ns = NormalStrength, bri = Brightness;
            float[] ox = GameModel.OutX, oy = GameModel.OutY, oz = GameModel.OutZ;
            int[] oc = GameModel.OutC;
            Parallel.For(0, n, i =>
            {
                float x = m.X[i], y = m.Y[i], z = m.Z[i];
                ox[i] = r0 * x + r1 * y + r2 * z;
                oy[i] = d0 * x + d1 * y + d2 * z;
                oz[i] = z;
                float mnx = m.NX[i], mny = m.NY[i], mnz = m.NZ[i];
                float wnx = nr0 * mnx + nr1 * mny + nr2 * mnz;
                float wny = nd0 * mnx + nd1 * mny + nd2 * mnz;
                float wnz = nf0 * mnx + nf1 * mny + nf2 * mnz;
                float hl = 0.5f + 0.5f * (wnx * lx + wny * ly + wnz * lz);   // half-Lambert
                float b = bri * ni * ((1f - ns) + ns * hl) + amb;
                oc[i] = Scale(m.C[i], b);
            });
        }

        // ---- CPU: full point-light path WITH shadows + shell cull -------
        // Mirrors the GPU shaders (GpuLighting) exactly so GPU-off looks the
        // same - just slower.  Builds are single-threaded (atomic-min scatter);
        // transform + shade are parallel.
        static void CpuSpotlight(VoxBatch m, int n,
            float r0, float r1, float r2, float d0, float d1, float d2,
            float nr0, float nr1, float nr2, float nd0, float nd1, float nd2, float nf0, float nf1, float nf2,
            float br, float bz)
        {
            float[] ox = GameModel.OutX, oy = GameModel.OutY, oz = GameModel.OutZ;
            int[] oc = GameModel.OutC;
            if (_wnx.Length < n) { _wnx = new float[n]; _wny = new float[n]; _wnz = new float[n]; }
            float[] wnx = _wnx, wny = _wny, wnz = _wnz;

            // transform positions + rotate normals into world space
            Parallel.For(0, n, i =>
            {
                float x = m.X[i], y = m.Y[i], z = m.Z[i];
                ox[i] = r0 * x + r1 * y + r2 * z;
                oy[i] = d0 * x + d1 * y + d2 * z;
                oz[i] = z;
                float mnx = m.NX[i], mny = m.NY[i], mnz = m.NZ[i];
                wnx[i] = nr0 * mnx + nr1 * mny + nr2 * mnz;
                wny[i] = nd0 * mnx + nd1 * mny + nd2 * mnz;
                wnz[i] = nf0 * mnx + nf1 * mny + nf2 * mnz;
            });

            // display-volume projection scales (match GpuLighting)
            float wxMin = -br, wyMin = -br, wzMin = -bz, wxMax = br, wyMax = br, wzMax = bz;
            float xSpan = 2 * br, ySpan = 2 * br, zSpan = 2 * bz;
            float uYZ = (ShellRes - 1) / ySpan, vYZ = (ShellRes - 1) / zSpan;
            float uXZ = (ShellRes - 1) / xSpan, vXZ = (ShellRes - 1) / zSpan;
            float uXY = (ShellRes - 1) / xSpan, vXY = (ShellRes - 1) / ySpan;
            float d2ix = 2_000_000_000f / xSpan, d2iy = 2_000_000_000f / ySpan, d2iz = 2_000_000_000f / zSpan;
            int shellCullI = (int)((CullThresholdTexels / ShellRes) * MathF.Max(d2ix, MathF.Max(d2iy, d2iz)));
            int S = ShellRes * ShellRes;

            // build 6-face shell depth (single-threaded atomic-min)
            if (_cpuShell.Length != 6 * S) _cpuShell = new int[6 * S];
            Array.Fill(_cpuShell, int.MaxValue);
            for (int i = 0; i < n; i++)
            {
                float bx = ox[i], by = oy[i], bzz = oz[i];
                int uyi = (int)((by - wyMin) * uYZ), uzi = (int)((bzz - wzMin) * vYZ);
                int uxi = (int)((bx - wxMin) * uXZ), vzi = (int)((bzz - wzMin) * vXZ);
                int ux2 = (int)((bx - wxMin) * uXY), vyi = (int)((by - wyMin) * vXY);
                if ((uint)uyi >= ShellRes || (uint)uzi >= ShellRes || (uint)uxi >= ShellRes ||
                    (uint)vzi >= ShellRes || (uint)ux2 >= ShellRes || (uint)vyi >= ShellRes) continue;
                Min(_cpuShell, 0 * S + uzi * ShellRes + uyi, (int)((wxMax - bx) * d2ix));
                Min(_cpuShell, 1 * S + uzi * ShellRes + uyi, (int)((bx - wxMin) * d2ix));
                Min(_cpuShell, 2 * S + vzi * ShellRes + uxi, (int)((wyMax - by) * d2iy));
                Min(_cpuShell, 3 * S + vzi * ShellRes + uxi, (int)((by - wyMin) * d2iy));
                Min(_cpuShell, 4 * S + vyi * ShellRes + ux2, (int)((wzMax - bzz) * d2iz));
                Min(_cpuShell, 5 * S + vyi * ShellRes + ux2, (int)((bzz - wzMin) * d2iz));
            }

            // build per-light shadow maps (single-threaded atomic-min)
            int smSize = ShadowRes * ShadowRes;
            if (_cpuShadow.Length != LightCount * smSize) _cpuShadow = new int[LightCount * smSize];
            Array.Fill(_cpuShadow, int.MaxValue);
            for (int k = 0; k < LightCount; k++)
            {
                int b = k * 24; if (_packed[b + 6] <= 0f) continue;
                float lrx = _packed[b + 11], lry = _packed[b + 12], lrz = _packed[b + 13];
                float lux = _packed[b + 14], luy = _packed[b + 15], luz = _packed[b + 16];
                float ldx = _packed[b + 8], ldy = _packed[b + 9], ldz = _packed[b + 10];
                float minU = _packed[b + 17], minV = _packed[b + 18], uScl = _packed[b + 19], vScl = _packed[b + 20];
                float dMin = _packed[b + 21], d2i = _packed[b + 22];
                int slice = k * smSize;
                for (int i = 0; i < n; i++)
                {
                    float bx = ox[i], by = oy[i], bzz = oz[i];
                    int cu = (int)((bx * lrx + by * lry + bzz * lrz - minU) * uScl);
                    int cv = (int)((bx * lux + by * luy + bzz * luz - minV) * vScl);
                    if ((uint)cu >= ShadowRes || (uint)cv >= ShadowRes) continue;
                    Min(_cpuShadow, slice + cv * ShadowRes + cu, (int)((bx * ldx + by * ldy + bzz * ldz - dMin) * d2i));
                }
            }

            // shade (parallel): shell cull + per-light diffuse/atten/shadow
            float amb = Ambient, bri = Brightness;
            float[] pk = _packed;
            Parallel.For(0, n, i =>
            {
                float px = ox[i], py = oy[i], pz = oz[i];
                int iuy = Clamp((int)((py - wyMin) * uYZ), ShellRes), iuz = Clamp((int)((pz - wzMin) * vYZ), ShellRes);
                int iux = Clamp((int)((px - wxMin) * uXZ), ShellRes), ivz = Clamp((int)((pz - wzMin) * vXZ), ShellRes);
                int iu2 = Clamp((int)((px - wxMin) * uXY), ShellRes), ivy = Clamp((int)((py - wyMin) * vXY), ShellRes);
                bool keep =
                    (int)((wxMax - px) * d2ix) <= _cpuShell[0 * S + iuz * ShellRes + iuy] + shellCullI ||
                    (int)((px - wxMin) * d2ix) <= _cpuShell[1 * S + iuz * ShellRes + iuy] + shellCullI ||
                    (int)((wyMax - py) * d2iy) <= _cpuShell[2 * S + ivz * ShellRes + iux] + shellCullI ||
                    (int)((py - wyMin) * d2iy) <= _cpuShell[3 * S + ivz * ShellRes + iux] + shellCullI ||
                    (int)((wzMax - pz) * d2iz) <= _cpuShell[4 * S + ivy * ShellRes + iu2] + shellCullI ||
                    (int)((pz - wzMin) * d2iz) <= _cpuShell[5 * S + ivy * ShellRes + iu2] + shellCullI;
                if (!keep) { ox[i] = 1e6f; oc[i] = 0; return; }

                float nx = wnx[i], ny = wny[i], nz = wnz[i];
                float nl = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                if (nl > 1e-6f) { nx /= nl; ny /= nl; nz /= nl; }

                float aR = 0f, aG = 0f, aB = 0f;
                for (int k = 0; k < LightCount; k++)
                {
                    int b = k * 24;
                    float intensity = pk[b + 6];
                    float lrad = MathF.Max(pk[b + 7], 0.01f);
                    float ldx = pk[b + 8], ldy = pk[b + 9], ldz = pk[b + 10];
                    float ndotl = MathF.Max(0f, -(nx * ldx + ny * ldy + nz * ldz));
                    float ddx = pk[b + 0] - px, ddy = pk[b + 1] - py, ddz = pk[b + 2] - pz;
                    float nd = MathF.Sqrt(ddx * ddx + ddy * ddy + ddz * ddz) / lrad;
                    float atten = intensity / (1f + nd * nd);
                    float su = px * pk[b + 11] + py * pk[b + 12] + pz * pk[b + 13];
                    float sv = px * pk[b + 14] + py * pk[b + 15] + pz * pk[b + 16];
                    float sd = px * pk[b + 8] + py * pk[b + 9] + pz * pk[b + 10];
                    int cu = Clamp((int)((su - pk[b + 17]) * pk[b + 19]), ShadowRes);
                    int cv = Clamp((int)((sv - pk[b + 18]) * pk[b + 20]), ShadowRes);
                    int myD = (int)((sd - pk[b + 21]) * pk[b + 22]);
                    int nearest = _cpuShadow[k * smSize + cv * ShadowRes + cu];
                    float sf = (myD > nearest + (int)pk[b + 23]) ? 0f : 1f;   // SFac = 0 (hard)
                    float c = atten * ndotl * sf;
                    aR += pk[b + 3] * c; aG += pk[b + 4] * c; aB += pk[b + 5] * c;
                }
                int col = m.C[i];
                float sr = ((col >> 16) & 0xFF) / 255f, sg = ((col >> 8) & 0xFF) / 255f, sb = (col & 0xFF) / 255f;
                oc[i] = (C255(sr * bri * (amb + aR) * 255f) << 16) | (C255(sg * bri * (amb + aG) * 255f) << 8) | C255(sb * bri * (amb + aB) * 255f);
            });
        }

        static void Min(int[] a, int i, int v) { if (v < a[i]) a[i] = v; }
        static int Clamp(int c, int res) => c < 0 ? 0 : c >= res ? res - 1 : c;

        static int Scale(int rgb, float b)
        {
            int r = C255(((rgb >> 16) & 0xFF) * b);
            int g = C255(((rgb >> 8) & 0xFF) * b);
            int bl = C255((rgb & 0xFF) * b);
            return (r << 16) | (g << 8) | bl;
        }
        static int C255(float v) => v <= 0f ? 0 : v >= 255f ? 255 : (int)v;

        // ---- pack lights for the GPU (cloned from VLEDStudio.PackLightData)
        // 24 floats/light: pos, colour, intensity, radius, dir, right, up,
        // then the display-volume shadow projection (minU/V, uv scale, minD,
        // depthToInt, biasInt).
        static void PackLightData(int lightCount, int sRes, float bndR, float bndZ)
        {
            Array.Clear(_packed);
            for (int k = 0; k < lightCount; k++)
            {
                Src L = _src[k];
                int dst = k * 24;
                _packed[dst + 0] = L.PX; _packed[dst + 1] = L.PY; _packed[dst + 2] = L.PZ;
                _packed[dst + 3] = L.R; _packed[dst + 4] = L.G; _packed[dst + 5] = L.B;
                _packed[dst + 6] = L.I; _packed[dst + 7] = L.Rad;

                float ldx = L.TX - L.PX, ldy = L.TY - L.PY, ldz = L.TZ - L.PZ;
                float llen = MathF.Sqrt(ldx * ldx + ldy * ldy + ldz * ldz);
                if (llen < 1e-6f) { ldx = 0; ldy = -1; ldz = 0; llen = 1; }
                ldx /= llen; ldy /= llen; ldz /= llen;

                float ux = 0, uy = 1, uz = 0;
                if (MathF.Abs(ldy) > 0.9f) { ux = 1; uy = 0; }
                float rx = ldy * uz - ldz * uy, ry = ldz * ux - ldx * uz, rz = ldx * uy - ldy * ux;
                float rl = MathF.Sqrt(rx * rx + ry * ry + rz * rz); if (rl < 1e-6f) rl = 1f;
                rx /= rl; ry /= rl; rz /= rl;
                float cux = ry * ldz - rz * ldy, cuy = rz * ldx - rx * ldz, cuz = rx * ldy - ry * ldx;

                _packed[dst + 8] = ldx; _packed[dst + 9] = ldy; _packed[dst + 10] = ldz;
                _packed[dst + 11] = rx; _packed[dst + 12] = ry; _packed[dst + 13] = rz;
                _packed[dst + 14] = cux; _packed[dst + 15] = cuy; _packed[dst + 16] = cuz;

                float minU = float.MaxValue, maxU = float.MinValue;
                float minV = float.MaxValue, maxV = float.MinValue;
                float minD = float.MaxValue, maxD = float.MinValue;
                for (int cx = -1; cx <= 1; cx += 2)
                    for (int cy = -1; cy <= 1; cy += 2)
                        for (int cz = -1; cz <= 1; cz += 2)
                        {
                            float wx = cx * bndR, wy = cy * bndR, wz = cz * bndZ;
                            float su = wx * rx + wy * ry + wz * rz;
                            float sv = wx * cux + wy * cuy + wz * cuz;
                            float sd = wx * ldx + wy * ldy + wz * ldz;
                            if (su < minU) minU = su; if (su > maxU) maxU = su;
                            if (sv < minV) minV = sv; if (sv > maxV) maxV = sv;
                            if (sd < minD) minD = sd; if (sd > maxD) maxD = sd;
                        }
                float uSpan = maxU - minU; if (uSpan < 1e-6f) uSpan = 1f;
                float vSpan = maxV - minV; if (vSpan < 1e-6f) vSpan = 1f;
                float dSpan = maxD - minD; if (dSpan < 1e-6f) dSpan = 1f;

                _packed[dst + 17] = minU;
                _packed[dst + 18] = minV;
                _packed[dst + 19] = (sRes - 1) / uSpan;
                _packed[dst + 20] = (sRes - 1) / vSpan;
                _packed[dst + 21] = minD;
                _packed[dst + 22] = 2_000_000_000f / dSpan;
                _packed[dst + 23] = ShadowBias * (2_000_000_000f / dSpan);
            }
        }
    }
}
