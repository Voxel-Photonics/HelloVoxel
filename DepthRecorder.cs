using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Voxon
{
    // =====================================================================
    //  DEPTH-MAP RECORDER  -  records the live volume to a VoxelStudio
    //  depth-map video (the /voxelstudio-depthmap format), so any demo built
    //  with this blueprint can be captured and played back in VoxelStudio.
    //
    //  Each frame the world-space voxels being drawn are encoded as a 6-face
    //  orthographic cubemap (colour band on top, 16-bit depth band below) and
    //  streamed as raw RGB24 to ffmpeg, which muxes them into a lossless
    //  FFV1 / MKV file.  Encoder cloned from VLEDStudio's VoxelDepthMap.
    //
    //  Frame layout (W = 6*faceRes, H = 2*faceRes):
    //    rows 0..faceRes-1        colour band  (R,G,B of nearest voxel / face)
    //    rows faceRes..2faceRes-1 depth band   (16-bit: R=hi, G=lo, B=0)
    //    six faces side by side:  +X +Y -X -Y Top Bottom
    //    empty pixel = (0,0,0)    (VoxelStudio skips these on decode)
    //
    //  Toggle with Ctrl+R or Game -> "Record Depth Video" (both land in
    //  Program.ToggleRecording -> Bridge.RecordStart/StopRequested).
    //  Files are named <APP NAME>_yyyy-MM-dd_HH-mm-ss.mkv (see AppName()).
    // =====================================================================
    internal static class DepthRecorder
    {
        const int Views = 6;
        const float Fps = 30f;                       // nominal playback rate

        static Process? _ff;
        static Stream? _in;
        static bool _recording;
        static int _faceRes, _w, _h;
        static float _br, _bz;
        static byte[] _raw = Array.Empty<byte>();
        static float[][] _depth = Array.Empty<float[]>();
        static int[][] _color = Array.Empty<int[]>();
        static int[] _viewH = new int[Views];
        static string _path = "";

        public static bool IsRecording => _recording;
        public static string LastPath => _path;

        // ---- start / stop -----------------------------------------------
        public static bool Start(float boundr, float boundz, string dir)
        {
            if (_recording) return true;
            try
            {
                bool xl = VoxDevice.IsXL(boundr);         // VX2-XL vs VX2 (bounds come from the DLL)
                _faceRes = xl ? 512 : 256;
                _br = boundr; _bz = boundz;
                _w = Views * _faceRes;
                _h = 2 * _faceRes;

                // per-view slot heights: XL side faces are half-height
                for (int v = 0; v < Views; v++)
                    _viewH[v] = (xl && v < 4) ? _faceRes / 2 : _faceRes;

                _raw = new byte[_w * _h * 3];
                _depth = new float[Views][];
                _color = new int[Views][];
                for (int v = 0; v < Views; v++)
                {
                    _depth[v] = new float[_viewH[v] * _faceRes];
                    _color[v] = new int[_viewH[v] * _faceRes];
                }

                if (string.IsNullOrWhiteSpace(dir)) dir = Path.Combine(AppContext.BaseDirectory, "recordings");
                Directory.CreateDirectory(dir);
                // File name = <APP NAME> + date/time, e.g. HelloVoxel_2026-07-29_14-30-12.mkv.
                // The name comes from the assembly, so an app cloned from this
                // blueprint names its recordings after itself with no edit here.
                _path = Path.Combine(dir, AppName() + "_" +
                    DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + ".mkv");

                string ffmpeg = FindFfmpeg();
                var psi = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    // raw RGB24 frames on stdin -> lossless FFV1 in an MKV
                    Arguments = string.Format(CultureInfo.InvariantCulture,
                        "-f rawvideo -pixel_format rgb24 -video_size {0}x{1} -framerate {2:F2} " +
                        "-i pipe:0 -c:v ffv1 -pix_fmt rgb24 -y \"{3}\"",
                        _w, _h, Fps, _path),
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                _ff = Process.Start(psi);
                if (_ff == null) return false;
                _in = _ff.StandardInput.BaseStream;
                _ff.ErrorDataReceived += (_, _) => { };   // drain stderr so ffmpeg never blocks
                _ff.BeginErrorReadLine();
                _recording = true;
                Console.WriteLine("[rec] recording depth-map video -> " + _path);
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("[rec] start failed (is ffmpeg installed / on PATH?): " + e.Message);
                Cleanup();
                return false;
            }
        }

        public static void Stop()
        {
            if (!_recording) return;
            _recording = false;
            try { _in?.Flush(); _in?.Close(); } catch { }
            try { if (_ff != null && !_ff.WaitForExit(15000)) _ff.Kill(); } catch { }
            Console.WriteLine("[rec] saved " + _path);
            Cleanup();
        }

        static void Cleanup()
        {
            try { _ff?.Dispose(); } catch { }
            _ff = null; _in = null;
        }

        // ---- per-frame capture ------------------------------------------
        // Encodes the current world-space voxels (bc==0 = culled/empty ->
        // skipped) into the cubemap frame and writes it to ffmpeg.
        public static void Capture(float[] bx, float[] by, float[] bz, int[] bc, int count)
        {
            if (!_recording || _in == null) return;
            try
            {
                Array.Clear(_raw);
                for (int v = 0; v < Views; v++)
                {
                    var d = _depth[v];
                    for (int i = 0; i < d.Length; i++) d[i] = float.NegativeInfinity;
                }

                int VW = _faceRes;
                float brInv = 1f / (2f * _br), bzInv = 1f / (2f * _bz);
                int hSide = _viewH[0], hTop = _viewH[4];

                // z-buffer each voxel into the 6 faces (keep the nearest surface)
                for (int i = 0; i < count; i++)
                {
                    int col = bc[i];
                    if (col == 0) continue;                       // empty / culled
                    float x = bx[i], y = by[i], z = bz[i];
                    if (x < -_br || x > _br || y < -_br || y > _br || z < -_bz || z > _bz) continue;

                    int cx = Clamp((int)((x + _br) * brInv * (VW - 1) + .5f), VW);
                    int cyc = Clamp((int)((y + _br) * brInv * (VW - 1) + .5f), VW);
                    int rz = Clamp((int)((z + _bz) * bzInv * (hSide - 1) + .5f), hSide);
                    int cyt = Clamp((int)((y + _br) * brInv * (hTop - 1) + .5f), hTop);

                    Put(0, rz * VW + cyc, x, col);    // +X
                    Put(1, rz * VW + cx, y, col);     // +Y
                    Put(2, rz * VW + cyc, -x, col);   // -X
                    Put(3, rz * VW + cx, -y, col);    // -Y
                    Put(4, cyt * VW + cx, -z, col);   // Top
                    Put(5, cyt * VW + cx, z, col);    // Bottom
                }

                // pack into the RGB24 frame (colour band + 16-bit depth band)
                int stride = _w * 3;
                for (int view = 0; view < Views; view++)
                {
                    int xOff = view * VW;
                    float dMin = view < 4 ? -_br : -_bz;
                    float dScale = 65535f / (view < 4 ? 2f * _br : 2f * _bz);
                    int vh = _viewH[view];
                    var d = _depth[view]; var c = _color[view];
                    for (int row = 0; row < vh; row++)
                    {
                        int colRow = row * stride + xOff * 3;
                        int depRow = (row + _faceRes) * stride + xOff * 3;
                        for (int px = 0; px < VW; px++)
                        {
                            float depth = d[row * VW + px];
                            bool hit = depth > float.NegativeInfinity;
                            int cc = c[row * VW + px];
                            int cp = colRow + px * 3;
                            if (hit)
                            {
                                byte r = (byte)(cc >> 16), g = (byte)(cc >> 8), b = (byte)cc;
                                if (r == 0 && g == 0 && b == 0) b = 1;   // avoid the empty sentinel
                                _raw[cp] = r; _raw[cp + 1] = g; _raw[cp + 2] = b;
                                ushort d16 = (ushort)Math.Clamp((depth - dMin) * dScale, 0, 65535);
                                int dp = depRow + px * 3;
                                _raw[dp] = (byte)(d16 >> 8); _raw[dp + 1] = (byte)(d16 & 0xFF);
                            }
                        }
                    }
                }

                _in.Write(_raw, 0, _raw.Length);
            }
            catch (Exception e)
            {
                Console.WriteLine("[rec] capture failed, stopping: " + e.Message);
                Stop();
            }
        }

        static void Put(int view, int idx, float depth, int col)
        {
            var d = _depth[view];
            if (depth > d[idx]) { d[idx] = depth; _color[view][idx] = col; }
        }

        static int Clamp(int v, int n) => v < 0 ? 0 : v >= n ? n - 1 : v;

        // ---- app name for the file name ---------------------------------
        // Assembly name first (stable however the app is launched), exe name as
        // a fallback for odd hosts; anything not legal in a file name is dropped.
        static string AppName()
        {
            string name = "";
            try { name = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? ""; } catch { }
            if (string.IsNullOrWhiteSpace(name))
                try { name = Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? ""; } catch { }
            if (string.IsNullOrWhiteSpace(name)) name = "VLED";
            foreach (char bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
            return name.Trim();
        }

        // ---- ffmpeg discovery (cloned from VLEDStudio) ------------------
        static string FindFfmpeg()
        {
            try
            {
                string winget = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "WinGet", "Packages");
                if (Directory.Exists(winget))
                {
                    string? hit = Directory.GetFiles(winget, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
                    if (hit != null) return hit;
                }
            }
            catch { }
            return "ffmpeg";   // rely on PATH
        }
    }
}
