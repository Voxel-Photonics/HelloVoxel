// ============================================================================
//  GpuLighting.cs  -  CLONED from VLEDStudio_Avalonia (the reference renderer),
//  adapted to the HelloVoxel namespace.
//
//  GPU-accelerated shadow-map + point-light pipeline using ComputeSharp
//  (DirectX 12 compute shaders).  The model is uploaded ONCE (model space);
//  every frame the GPU applies the rotation/scale + normal rotation, fills the
//  shell + per-light shadow maps (atomic-min), and shades - so a static model
//  (even ~1M voxels) never re-uploads.  That is the whole reason it is fast.
//
//  Pipeline per frame:
//    1. TransformAndNormalShader - model->world positions + rotate normals
//    2. ShellDepthFillShader     - 6-face orthographic depth (interior cull)
//    3. PtLightDepthFillShader   - atomic-min shadow map, one dispatch / light
//    4. PointLightShader         - shell cull + coloured point lights + shadows
//
//  Falls back to the caller's CPU path when no DX12-capable GPU is present.
// ============================================================================

using System;
using System.Diagnostics;
using ComputeSharp;

namespace Voxon
{
    static class GpuLighting
    {
        static GraphicsDevice? _device;
        static bool _available = true;

        // Persistent GPU buffers - resized on demand
        static ReadWriteBuffer<float>? _mxB, _myB, _mzB;
        static ReadWriteBuffer<float>? _mnxB, _mnyB, _mnzB;
        static ReadWriteBuffer<int>? _mcB;
        static ReadWriteBuffer<float>? _bxB, _byB, _bzB;
        static ReadWriteBuffer<int>? _bcB;
        static ReadWriteBuffer<float>? _lsuB, _lsvB, _lsdB;
        static ReadWriteBuffer<int>? _smB;
        static ReadWriteBuffer<int>? _shellSmB;
        static ReadWriteBuffer<float>? _lightsB;
        static ReadWriteBuffer<int>? _ptSmB;
        static int _allocN, _allocSM, _allocShellSM, _allocPtSM;
        static bool _meshDirty = true;
        static bool _mcDirty = true;
        static int _cachedN;
        static float[] _lightPadBuf = new float[8 * 24];

        // Packed mesh buffer: one ReadWriteBuffer<float> of 6*N floats laid out
        // as [X|Y|Z|NX|NY|NZ] segments, so a static model is one big CopyFrom
        // (only when dirty) instead of six small ones each frame.
        static ReadWriteBuffer<float>? _meshDataB;
        static float[] _meshDataCpu = Array.Empty<float>();
        static float[] _lastLightUploaded = Array.Empty<float>();

        // Packed output positions: one ReadWriteBuffer<float> of 3*N floats
        // [BX|BY|BZ]; one CopyTo back + CPU de-interleave.
        static ReadWriteBuffer<float>? _bDataB;
        static float[] _bDataCpu = Array.Empty<float>();

        /// <summary>Call when model data (positions/normals/colors) changes.</summary>
        public static void MarkMeshDirty() { _meshDirty = true; _mcDirty = true; }
        /// <summary>Call when ONLY the colour buffer changed.</summary>
        public static void MarkColorsDirty() { _mcDirty = true; }

        static int[]? _ptSmClearBuf;
        static int[]? _clearBuf;
        static int[]? _shellClearBuf;

        public static bool IsAvailable => _available;

        // Shell z-buffer resolution, fixed independently of shadow map res.
        const int ShellRes = 512;

        static bool EnsureDevice()
        {
            if (!_available) return false;
            if (_device != null) return true;
            try { _device = GraphicsDevice.GetDefault(); return true; }
            catch { _available = false; return false; }
        }

        static void EnsureBuffers(int n, int smSize)
        {
            var d = _device!;
            if (n > _allocN)
            {
                _mxB?.Dispose(); _myB?.Dispose(); _mzB?.Dispose();
                _mnxB?.Dispose(); _mnyB?.Dispose(); _mnzB?.Dispose();
                _mcB?.Dispose();
                _bxB?.Dispose(); _byB?.Dispose(); _bzB?.Dispose();
                _bcB?.Dispose();
                _lsuB?.Dispose(); _lsvB?.Dispose(); _lsdB?.Dispose();

                _mxB = d.AllocateReadWriteBuffer<float>(n);
                _myB = d.AllocateReadWriteBuffer<float>(n);
                _mzB = d.AllocateReadWriteBuffer<float>(n);
                _mnxB = d.AllocateReadWriteBuffer<float>(n);
                _mnyB = d.AllocateReadWriteBuffer<float>(n);
                _mnzB = d.AllocateReadWriteBuffer<float>(n);
                _mcB = d.AllocateReadWriteBuffer<int>(n);
                _bxB = d.AllocateReadWriteBuffer<float>(n);
                _byB = d.AllocateReadWriteBuffer<float>(n);
                _bzB = d.AllocateReadWriteBuffer<float>(n);
                _bcB = d.AllocateReadWriteBuffer<int>(n);
                _lsuB = d.AllocateReadWriteBuffer<float>(n);
                _lsvB = d.AllocateReadWriteBuffer<float>(n);
                _lsdB = d.AllocateReadWriteBuffer<float>(n);
                _allocN = n;

                _meshDataB?.Dispose();
                _meshDataB = d.AllocateReadWriteBuffer<float>(6 * n);
                _meshDataCpu = new float[6 * n];

                _bDataB?.Dispose();
                _bDataB = d.AllocateReadWriteBuffer<float>(3 * n);
                _bDataCpu = new float[3 * n];
            }

            if (smSize > _allocSM)
            {
                _smB?.Dispose();
                _smB = d.AllocateReadWriteBuffer<int>(smSize);
                _clearBuf = new int[smSize];
                Array.Fill(_clearBuf, int.MaxValue);
                _allocSM = smSize;
            }

            int shellSize = 6 * ShellRes * ShellRes;
            if (shellSize > _allocShellSM)
            {
                _shellSmB?.Dispose();
                _shellSmB = d.AllocateReadWriteBuffer<int>(shellSize);
                _shellClearBuf = new int[shellSize];
                Array.Fill(_shellClearBuf, int.MaxValue);
                _allocShellSM = shellSize;
            }
        }

        // D3D12 allows at most 65535 thread groups per dispatch dimension, and
        // ComputeSharp's 1D default is 64 threads/group, so a single For() can
        // cover at most 64 * 65535 elements.  The shadow atlas is larger than
        // that (MaxLights * 1024^2 = 8M ints), and the resulting
        // "groupsX ... 131072" ArgumentOutOfRangeException used to knock the
        // whole GPU path out to the CPU fallback on the first frame.  Clear it
        // in slices instead.
        const int ClearGroupSize = 64;                              // DefaultThreadGroupSizes.X
        const int MaxDispatchGroups = 65535;                        // D3D12 per-dimension limit
        const int MaxClearChunk = ClearGroupSize * MaxDispatchGroups;

        static void ClearIntBuffer(ReadWriteBuffer<int> buf, int count, int value)
        {
            for (int off = 0; off < count; off += MaxClearChunk)
                _device!.For(Math.Min(MaxClearChunk, count - off),
                             new ClearIntBufferShader { Buf = buf, Offset = off, Value = value });
        }

        /// <summary>
        /// GPU pipeline for point-light mode with shadow casting.
        /// packedLightData: 24 floats per light -
        ///   [0-2] posXYZ, [3-5] colorRGB, [6] intensity, [7] radius,
        ///   [8-10] lightDir, [11-13] lightRight, [14-16] lightUp,
        ///   [17] minU, [18] minV, [19] uScale, [20] vScale,
        ///   [21] minD, [22] depthToInt, [23] biasInt (as float)
        /// </summary>
        public static bool RunPointLights(
            float[] mx, float[] my, float[] mz,
            float[] mnx, float[] mny, float[] mnz,
            int[] mc, int n,
            float r0, float r1, float r2,
            float d0, float d1, float d2,
            float f0, float f1, float f2,
            float px, float py, float pz,
            float nr0, float nr1, float nr2,
            float nd0, float nd1, float nd2,
            float nf0, float nf1, float nf2,
            float[] packedLightData, int lightCount,
            int shadowRes,
            float ambient, float brightness,
            float[] bx, float[] by, float[] bz, int[] bc,
            float boundr = 1f, float boundz = 1f)
        {
            if (!EnsureDevice()) return false;
            try
            {
                EnsureBuffers(n, 1);

                float wxMin = -boundr, wxMax = boundr;
                float wyMin = -boundr, wyMax = boundr;
                float wzMin = -boundz, wzMax = boundz;
                float xSpan = wxMax - wxMin; if (xSpan < 1e-6f) xSpan = 1f;
                float ySpan = wyMax - wyMin; if (ySpan < 1e-6f) ySpan = 1f;
                float zSpan = wzMax - wzMin; if (zSpan < 1e-6f) zSpan = 1f;

                int shellSmSize = ShellRes * ShellRes;
                float uScaleYZ = (ShellRes - 1) / ySpan, vScaleYZ = (ShellRes - 1) / zSpan;
                float uScaleXZ = (ShellRes - 1) / xSpan, vScaleXZ = (ShellRes - 1) / zSpan;
                float uScaleXY = (ShellRes - 1) / xSpan, vScaleXY = (ShellRes - 1) / ySpan;
                float d2ix = 2_000_000_000f / xSpan;
                float d2iy = 2_000_000_000f / ySpan;
                float d2iz = 2_000_000_000f / zSpan;
                const float CullThresholdTexels = 4.0f;
                const float CullThreshold = CullThresholdTexels / ShellRes;
                int shellCullI = (int)(CullThreshold * MathF.Max(d2ix, MathF.Max(d2iy, d2iz)));

                const int LightStride = 24;
                const int MaxLights = 8;
                int lightBufSize = MaxLights * LightStride;
                if (_lightsB == null)
                    _lightsB = _device!.AllocateReadWriteBuffer<float>(lightBufSize);

                int ptSmSize = MaxLights * shadowRes * shadowRes;
                if (ptSmSize > _allocPtSM)
                {
                    _ptSmB?.Dispose();
                    _ptSmB = _device!.AllocateReadWriteBuffer<int>(ptSmSize);
                    _ptSmClearBuf = new int[ptSmSize];
                    Array.Fill(_ptSmClearBuf, int.MaxValue);
                    _allocPtSM = ptSmSize;
                }

                // Upload the model only when it changed (static models skip this).
                if (_meshDirty || n != _cachedN)
                {
                    Array.Copy(mx, 0, _meshDataCpu, 0 * n, n);
                    Array.Copy(my, 0, _meshDataCpu, 1 * n, n);
                    Array.Copy(mz, 0, _meshDataCpu, 2 * n, n);
                    Array.Copy(mnx, 0, _meshDataCpu, 3 * n, n);
                    Array.Copy(mny, 0, _meshDataCpu, 4 * n, n);
                    Array.Copy(mnz, 0, _meshDataCpu, 5 * n, n);
                    _meshDataB!.CopyFrom(_meshDataCpu.AsSpan(0, 6 * n));
                    _mcB!.CopyFrom(mc.AsSpan(0, n));
                    _meshDirty = false; _mcDirty = false; _cachedN = n;
                }
                else if (_mcDirty)
                {
                    _mcB!.CopyFrom(mc.AsSpan(0, n));
                    _mcDirty = false;
                }

                // Upload packed lights only when they changed.
                Array.Clear(_lightPadBuf);
                int copyLen = Math.Min(packedLightData.Length, lightBufSize);
                Array.Copy(packedLightData, _lightPadBuf, copyLen);
                if (_lastLightUploaded.Length != lightBufSize)
                    _lastLightUploaded = new float[lightBufSize];
                if (!_lightPadBuf.AsSpan(0, lightBufSize).SequenceEqual(_lastLightUploaded.AsSpan(0, lightBufSize)))
                {
                    _lightsB!.CopyFrom(_lightPadBuf.AsSpan(0, lightBufSize));
                    Array.Copy(_lightPadBuf, _lastLightUploaded, lightBufSize);
                }

                // Clear shadow + shell maps on GPU.  The atlas is allocated for
                // MaxLights but only the slices actually in use are read, so clear
                // just those - at ShadowRes 1024 that is 2M ints instead of 8M.
                int ptClearSize = Math.Min(ptSmSize, Math.Max(1, lightCount) * shadowRes * shadowRes);
                ClearIntBuffer(_ptSmB!, ptClearSize, int.MaxValue);
                ClearIntBuffer(_shellSmB!, 6 * shellSmSize, int.MaxValue);

                // Dispatch 1: transform + rotate normals.
                _device!.For(n, new TransformAndNormalShader
                {
                    MeshData = _meshDataB!,
                    N = n,
                    MC = _mcB!,
                    BData = _bDataB!,
                    OutNx = _lsuB!, OutNy = _lsvB!, OutNz = _lsdB!,
                    OutBc = _bcB!,
                    R0 = r0, R1 = r1, R2 = r2,
                    D0 = d0, D1 = d1, D2 = d2,
                    F0 = f0, F1 = f1, F2 = f2,
                    PX = px, PY = py, PZ = pz,
                    NR0 = nr0, NR1 = nr1, NR2 = nr2,
                    ND0 = nd0, ND1 = nd1, ND2 = nd2,
                    NF0 = nf0, NF1 = nf1, NF2 = nf2,
                });

                // Dispatch 1b: shell 6-face depth fill.
                _device.For(n, new ShellDepthFillShader
                {
                    BData = _bDataB!,
                    N = n,
                    SSM = _shellSmB!,
                    Res = ShellRes, SmSize = shellSmSize, Faces = 0x3F,
                    WxMin = wxMin, WyMin = wyMin, WzMin = wzMin,
                    WxMax = wxMax, WyMax = wyMax, WzMax = wzMax,
                    UScYZ = uScaleYZ, VScYZ = vScaleYZ,
                    UScXZ = uScaleXZ, VScXZ = vScaleXZ,
                    UScXY = uScaleXY, VScXY = vScaleXY,
                    D2Ix = d2ix, D2Iy = d2iy, D2Iz = d2iz,
                });

                // Dispatch 2: per-light shadow-map fill (skip disabled lights).
                for (int k = 0; k < lightCount; k++)
                {
                    int lb = k * LightStride;
                    if (packedLightData[lb + 6] <= 0f) continue;
                    _device.For(n, new PtLightDepthFillShader
                    {
                        BData = _bDataB!,
                        N = n,
                        SM = _ptSmB!,
                        LDx = packedLightData[lb + 8], LDy = packedLightData[lb + 9], LDz = packedLightData[lb + 10],
                        LRx = packedLightData[lb + 11], LRy = packedLightData[lb + 12], LRz = packedLightData[lb + 13],
                        LUx = packedLightData[lb + 14], LUy = packedLightData[lb + 15], LUz = packedLightData[lb + 16],
                        MinU = packedLightData[lb + 17], MinV = packedLightData[lb + 18],
                        UScl = packedLightData[lb + 19], VScl = packedLightData[lb + 20],
                        DMin = packedLightData[lb + 21], D2I = packedLightData[lb + 22],
                        Res = shadowRes,
                        SliceOffset = k * shadowRes * shadowRes,
                    });
                }

                // Dispatch 3: shell cull + shade with shadow lookup.
                _device.For(n, new PointLightShader
                {
                    BData = _bDataB!,
                    N = n,
                    NX = _lsuB!, NY = _lsvB!, NZ = _lsdB!,
                    MC = _mcB!, OutBc = _bcB!,
                    Lights = _lightsB!,
                    SM = _ptSmB!,
                    SSM = _shellSmB!,
                    LightCount = lightCount,
                    ShadowRes = shadowRes,
                    SFac = 0f,
                    Amb = ambient, Bri = brightness,
                    SRes = ShellRes, SmSize = shellSmSize, Faces = 0x3F,
                    CullI = shellCullI,
                    WxMin = wxMin, WyMin = wyMin, WzMin = wzMin,
                    WxMax = wxMax, WyMax = wyMax, WzMax = wzMax,
                    UScYZ = uScaleYZ, VScYZ = vScaleYZ,
                    UScXZ = uScaleXZ, VScXZ = vScaleXZ,
                    UScXY = uScaleXY, VScXY = vScaleXY,
                    D2Ix = d2ix, D2Iy = d2iy, D2Iz = d2iz,
                });

                // Download: one CopyTo of packed positions, de-interleave, + colours.
                _bDataB!.CopyTo(_bDataCpu.AsSpan(0, 3 * n));
                Array.Copy(_bDataCpu, 0 * n, bx, 0, n);
                Array.Copy(_bDataCpu, 1 * n, by, 0, n);
                Array.Copy(_bDataCpu, 2 * n, bz, 0, n);
                _bcB!.CopyTo(bc.AsSpan(0, n));
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[gpu] disabled after error, using CPU: " + ex.Message);
                _available = false;
                return false;
            }
        }
    }

    // ---- set an int buffer to a constant --------------------------------
    // Offset lets a big buffer be cleared in several dispatches - see
    // GpuLighting.ClearIntBuffer for why that is necessary.
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    internal partial struct ClearIntBufferShader : IComputeShader
    {
        public ReadWriteBuffer<int> Buf;
        public int Offset;
        public int Value;
        public void Execute() => Buf[Offset + ThreadIds.X] = Value;
    }

    // ---- Transform + normal rotation (model -> world) -------------------
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    internal partial struct TransformAndNormalShader : IComputeShader
    {
        public ReadWriteBuffer<float> MeshData;   // 6*N: [X|Y|Z|NX|NY|NZ]
        public int N;
        public ReadWriteBuffer<int> MC;
        public ReadWriteBuffer<float> BData;      // 3*N: [BX|BY|BZ]
        public ReadWriteBuffer<float> OutNx, OutNy, OutNz;
        public ReadWriteBuffer<int> OutBc;
        public float R0, R1, R2, D0, D1, D2, F0, F1, F2;   // rotation x scale
        public float PX, PY, PZ;                            // translation
        public float NR0, NR1, NR2, ND0, ND1, ND2, NF0, NF1, NF2;  // pure rotation

        public void Execute()
        {
            int i = ThreadIds.X;
            float x = MeshData[i];
            float y = MeshData[i + N];
            float z = MeshData[i + 2 * N];
            BData[i] = R0 * x + R1 * y + R2 * z + PX;
            BData[i + N] = D0 * x + D1 * y + D2 * z + PY;
            BData[i + 2 * N] = F0 * x + F1 * y + F2 * z + PZ;
            OutBc[i] = MC[i];
            float nx = MeshData[i + 3 * N];
            float ny = MeshData[i + 4 * N];
            float nz = MeshData[i + 5 * N];
            OutNx[i] = NR0 * nx + NR1 * ny + NR2 * nz;
            OutNy[i] = ND0 * nx + ND1 * ny + ND2 * nz;
            OutNz[i] = NF0 * nx + NF1 * ny + NF2 * nz;
        }
    }

    // ---- Shell: 6-face orthographic depth fill (atomic min) -------------
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    internal partial struct ShellDepthFillShader : IComputeShader
    {
        public ReadWriteBuffer<float> BData;
        public int N;
        public ReadWriteBuffer<int> SSM;
        public int Res, SmSize, Faces;
        public float WxMin, WyMin, WzMin;
        public float WxMax, WyMax, WzMax;
        public float UScYZ, VScYZ;
        public float UScXZ, VScXZ;
        public float UScXY, VScXY;
        public float D2Ix, D2Iy, D2Iz;

        public void Execute()
        {
            int i = ThreadIds.X;
            float bx = BData[i], by = BData[i + N], bz = BData[i + 2 * N];
            int uyi = (int)((by - WyMin) * UScYZ);
            int uzi = (int)((bz - WzMin) * VScYZ);
            int uxi = (int)((bx - WxMin) * UScXZ);
            int vzi = (int)((bz - WzMin) * VScXZ);
            int ux2i = (int)((bx - WxMin) * UScXY);
            int vyi = (int)((by - WyMin) * VScXY);
            if (uyi < 0 || uyi >= Res || uzi < 0 || uzi >= Res ||
                uxi < 0 || uxi >= Res || vzi < 0 || vzi >= Res ||
                ux2i < 0 || ux2i >= Res || vyi < 0 || vyi >= Res)
                return;
            if ((Faces & 1) != 0)
                Hlsl.InterlockedMin(ref SSM[0 * SmSize + uzi * Res + uyi], (int)((WxMax - bx) * D2Ix));
            if ((Faces & 2) != 0)
                Hlsl.InterlockedMin(ref SSM[1 * SmSize + uzi * Res + uyi], (int)((bx - WxMin) * D2Ix));
            if ((Faces & 4) != 0)
                Hlsl.InterlockedMin(ref SSM[2 * SmSize + vzi * Res + uxi], (int)((WyMax - by) * D2Iy));
            if ((Faces & 8) != 0)
                Hlsl.InterlockedMin(ref SSM[3 * SmSize + vzi * Res + uxi], (int)((by - WyMin) * D2Iy));
            if ((Faces & 16) != 0)
                Hlsl.InterlockedMin(ref SSM[4 * SmSize + vyi * Res + ux2i], (int)((WzMax - bz) * D2Iz));
            if ((Faces & 32) != 0)
                Hlsl.InterlockedMin(ref SSM[5 * SmSize + vyi * Res + ux2i], (int)((bz - WzMin) * D2Iz));
        }
    }

    // ---- Point-light shadow-map fill (one dispatch per light) ----------
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    internal partial struct PtLightDepthFillShader : IComputeShader
    {
        public ReadWriteBuffer<float> BData;
        public int N;
        public ReadWriteBuffer<int> SM;
        public float LDx, LDy, LDz;
        public float LRx, LRy, LRz;
        public float LUx, LUy, LUz;
        public float MinU, MinV, UScl, VScl;
        public float DMin, D2I;
        public int Res;
        public int SliceOffset;

        public void Execute()
        {
            int i = ThreadIds.X;
            float bx = BData[i], by = BData[i + N], bz = BData[i + 2 * N];
            float u = bx * LRx + by * LRy + bz * LRz;
            float v = bx * LUx + by * LUy + bz * LUz;
            float d = bx * LDx + by * LDy + bz * LDz;
            int cui = (int)((u - MinU) * UScl);
            int cvi = (int)((v - MinV) * VScl);
            if (cui < 0 || cui >= Res || cvi < 0 || cvi >= Res) return;
            int intD = (int)((d - DMin) * D2I);
            Hlsl.InterlockedMin(ref SM[SliceOffset + cvi * Res + cui], intD);
        }
    }

    // ---- Point-light shading with shell cull + shadow lookup -----------
    [ThreadGroupSize(DefaultThreadGroupSizes.X)]
    [GeneratedComputeShaderDescriptor]
    internal partial struct PointLightShader : IComputeShader
    {
        public ReadWriteBuffer<float> BData;
        public int N;
        public ReadWriteBuffer<float> NX, NY, NZ;
        public ReadWriteBuffer<int> MC;
        public ReadWriteBuffer<int> OutBc;
        public ReadWriteBuffer<float> Lights;
        public ReadWriteBuffer<int> SM;
        public ReadWriteBuffer<int> SSM;
        public int LightCount;
        public int ShadowRes;
        public float SFac;
        public float Amb;
        public float Bri;
        public int SRes, SmSize, Faces, CullI;
        public float WxMin, WyMin, WzMin;
        public float WxMax, WyMax, WzMax;
        public float UScYZ, VScYZ;
        public float UScXZ, VScXZ;
        public float UScXY, VScXY;
        public float D2Ix, D2Iy, D2Iz;

        public void Execute()
        {
            int i = ThreadIds.X;
            float px = BData[i], py = BData[i + N], pz = BData[i + 2 * N];
            float nx = NX[i], ny = NY[i], nz = NZ[i];

            // Shell visibility test - cull interior geometry.
            int iuy = Hlsl.Max(0, Hlsl.Min((int)((py - WyMin) * UScYZ), SRes - 1));
            int iuz = Hlsl.Max(0, Hlsl.Min((int)((pz - WzMin) * VScYZ), SRes - 1));
            int iux = Hlsl.Max(0, Hlsl.Min((int)((px - WxMin) * UScXZ), SRes - 1));
            int ivz = Hlsl.Max(0, Hlsl.Min((int)((pz - WzMin) * VScXZ), SRes - 1));
            int iux2 = Hlsl.Max(0, Hlsl.Min((int)((px - WxMin) * UScXY), SRes - 1));
            int ivy = Hlsl.Max(0, Hlsl.Min((int)((py - WyMin) * VScXY), SRes - 1));

            bool keep = false;
            if ((Faces & 1) != 0 && (int)((WxMax - px) * D2Ix) <= SSM[0 * SmSize + iuz * SRes + iuy] + CullI) keep = true;
            if (!keep && (Faces & 2) != 0 && (int)((px - WxMin) * D2Ix) <= SSM[1 * SmSize + iuz * SRes + iuy] + CullI) keep = true;
            if (!keep && (Faces & 4) != 0 && (int)((WyMax - py) * D2Iy) <= SSM[2 * SmSize + ivz * SRes + iux] + CullI) keep = true;
            if (!keep && (Faces & 8) != 0 && (int)((py - WyMin) * D2Iy) <= SSM[3 * SmSize + ivz * SRes + iux] + CullI) keep = true;
            if (!keep && (Faces & 16) != 0 && (int)((WzMax - pz) * D2Iz) <= SSM[4 * SmSize + ivy * SRes + iux2] + CullI) keep = true;
            if (!keep && (Faces & 32) != 0 && (int)((pz - WzMin) * D2Iz) <= SSM[5 * SmSize + ivy * SRes + iux2] + CullI) keep = true;

            if (!keep)
            {
                BData[i] = 1e6f;   // push out of the volume -> culled by the draw
                OutBc[i] = 0;
                return;
            }

            float nLen = Hlsl.Sqrt(nx * nx + ny * ny + nz * nz);
            if (nLen > 1e-6f) { nx /= nLen; ny /= nLen; nz /= nLen; }

            int smSize = ShadowRes * ShadowRes;
            float accumR = 0f, accumG = 0f, accumB = 0f;

            for (int k = 0; k < LightCount; k++)
            {
                int b = k * 24;
                float lcr = Lights[b + 3], lcg = Lights[b + 4], lcb = Lights[b + 5];
                float intensity = Lights[b + 6];
                float lrad = Hlsl.Max(Lights[b + 7], 0.01f);
                float slpx = Lights[b + 0], slpy = Lights[b + 1], slpz = Lights[b + 2];
                float ldx = Lights[b + 8], ldy = Lights[b + 9], ldz = Lights[b + 10];
                float lrx = Lights[b + 11], lry = Lights[b + 12], lrz = Lights[b + 13];
                float lux = Lights[b + 14], luy = Lights[b + 15], luz = Lights[b + 16];

                float ndotl = Hlsl.Max(0f, -(nx * ldx + ny * ldy + nz * ldz));
                float ddx = slpx - px, ddy = slpy - py, ddz = slpz - pz;
                float nd = Hlsl.Sqrt(ddx * ddx + ddy * ddy + ddz * ddz) / lrad;
                float atten = intensity * (1f / (1f + nd * nd));

                float minU = Lights[b + 17], minV = Lights[b + 18];
                float uScl = Lights[b + 19], vScl = Lights[b + 20];
                float dMin = Lights[b + 21], d2i = Lights[b + 22];
                int biasI = (int)Lights[b + 23];

                float su = px * lrx + py * lry + pz * lrz;
                float sv = px * lux + py * luy + pz * luz;
                float sd = px * ldx + py * ldy + pz * ldz;
                int cu = Hlsl.Max(0, Hlsl.Min((int)((su - minU) * uScl), ShadowRes - 1));
                int cv = Hlsl.Max(0, Hlsl.Min((int)((sv - minV) * vScl), ShadowRes - 1));
                int myD = (int)((sd - dMin) * d2i);
                int nearest = SM[k * smSize + cv * ShadowRes + cu];
                float sf = (myD > nearest + biasI) ? SFac : 1f;

                float contrib = atten * ndotl * sf;
                accumR += lcr * contrib;
                accumG += lcg * contrib;
                accumB += lcb * contrib;
            }

            int col = MC[i];
            float sr = ((col >> 16) & 0xFF) / 255f;
            float sg = ((col >> 8) & 0xFF) / 255f;
            float sb = (col & 0xFF) / 255f;
            float fr = sr * Bri * (Amb + accumR);
            float fg = sg * Bri * (Amb + accumG);
            float fb = sb * Bri * (Amb + accumB);
            int cr = Hlsl.Max(0, Hlsl.Min((int)(fr * 255f), 255));
            int cg = Hlsl.Max(0, Hlsl.Min((int)(fg * 255f), 255));
            int cb = Hlsl.Max(0, Hlsl.Min((int)(fb * 255f), 255));
            OutBc[i] = (cr << 16) | (cg << 8) | cb;
        }
    }
}
