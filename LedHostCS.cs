#define DefineNativeDLLMethods      

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

/// <summary> LedHostCS.cs is a C# wrapper for LedHost.dll
/// LedHost.dll is a C++ library that provides a set of functions to interact with the Voxon VLED volumetric display.
/// LedHostCS.cs It will load in the LedHost.dll and provide a set of functions to interact with the Voxon volumetric display.
///
/// </summary>

// LedHostCS for Runtime 0.4.7

namespace Voxon
{

    // Windows standard library loaders. Importers set as separate Class... 
#if DefineNativeDLLMethods
    static class NativeMethods
    {
        #region DLL_imports
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        public static extern IntPtr LoadLibrary(string libname);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        public static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        public static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);
        #endregion

    }
#endif

#if !NET6_0_OR_GREATER
    public static class MathF
    {
        public static float Sin(float x) => (float)Math.Sin(x);
        public static float Cos(float x) => (float)Math.Cos(x);
        public static float Sqrt(float x) => (float)Math.Sqrt(x);
        public static float Pow(float x, float y) => (float)Math.Pow(x, y);
    }
#endif
    public class LedHostCS : IDisposable
    {
        public static IntPtr LedHostHandle;
        public int LedHostIniInitialised = 0;
        public bool AreDelegatesMapped = false;
        

        private bool LedhostHasUninitalised = false;
        private bool isMotorRunning = false;
        private int intendedRPM = 0;
        private string LedHostDLLPath = string.Empty;
        private Dictionary<string, bool> delegateMappingStatus = new Dictionary<string, bool>();

        // LedHost Delegates
        #region VXL delegate_functions
        // if new functions are added to the LedHost.dll, they need to be added here...

        internal delegate long vxl_getver_d();
        internal delegate void vxl_init_d(ref vxl_state_t vs);
        internal delegate int vxl_loadini_d(ref vxl_state_t vs);
        internal delegate void vxl_frame_start_d(ref vxl_state_t vs);
        internal delegate void vxl_frame_end_d(ref vxl_state_t vs);
        internal delegate int vxl_saveply_d(ref vxl_state_t vs, string filename);         // NOTE strings might need [MarshalAs(UnmanagedType.LPStr)] string filename...
        internal delegate void vxl_rend2d_cam_d(ref vxl_state_t vs, ref tiletype tt, ref cam_t cam);
        internal delegate void vxl_rend2d_d(ref vxl_state_t vs, ref tiletype tt, float emuhang, float emuvang, float emudist);
        internal delegate void vxl_uninit_d();
        internal delegate int vxl_drawvox_d(ref vxl_state_t vs, float wx, float wy, float wz, int col);
        internal delegate void vxl_drawvox_batch_d(ref vxl_state_t vs, ref float wxArray, ref float wyArray, ref float wzArray, ref int colArray, int arraySize, int flags);
        internal delegate void vxl_drawlin_d(ref vxl_state_t vs, float x0, float y0, float z0, float x1, float y1, float z1, int col);
        internal delegate void vxl_drawsph_d(ref vxl_state_t vs, float cx, float cy, float cz, float rad, int issol, int col);
        internal delegate void vxl_drawpol_d(ref vxl_state_t vs, ref tiletype tex, poltex[] pt, int n, int col); //poly must be convex
        internal delegate int vxl_drawspr_d(ref vxl_state_t vs, string filnam, ref point3d pp, ref point3d rr, ref point3d dd, ref point3d ff, int colsc, float forceScale);
        internal delegate int vxl_drawspr_getext_d(ref vxl_state_t vs, string filnam, ref point3d extmin, ref point3d extmax); // Get extentsvxl_drawspr_getext()
        internal delegate int vxl_drawzip_d(ref vxl_state_t vs, string filnam, ref point3d pp, ref point3d rr, ref point3d dd, ref point3d ff, int col, ref vxl_anim_t refAnim, double dtim);
        internal delegate void vxl_printalph_d(ref vxl_state_t vs, ref point3d p, ref point3d r, ref point3d d, float rad, int col, string str);
        internal delegate void vxl_pdrawvox_d(ref vxl_state_t vs, int ix, int iy, int iz, int col);
        internal delegate void vxl_pdrawbox_d(ref vxl_state_t vs, int x0, int y0, int z0, int x1, int y1, int z1, int col);
        internal delegate void vxl_pdrawtxt_d(ref vxl_state_t vs, ref tiletype custFont, int x0, int y0, int z0, int zsc, int col, string st);
        internal delegate void vxl_setrpm_d(ref vxl_state_t vs, int newrpm);
        internal delegate void vxl_setvxmodel_d(ref vxl_state_t vs, int model);
        internal delegate int vxl_getrpm_d(ref vxl_state_t vs);
        internal delegate int vxl_nav_read_d(int id, ref vxl_nav_t nav);
        internal delegate int vxl_joy_read_d(int usejoy, int id, ref vxl_joy_t vx);
        internal delegate void vxl_xbox_write_d(int joyid, float leftMotorValue, float rightMotorValue);
        internal delegate int vxl_vpen_read_d(ref vxl_vpen_t vpen, int maxpen);
        #endregion
        #region delegate_instances

        internal vxl_getver_d GetVer;
        internal vxl_init_d Init;
        internal vxl_loadini_d vxl_loadini;
        internal vxl_frame_start_d vxl_frame_start;
        internal vxl_frame_end_d FrameEnd;
        internal vxl_saveply_d SavePly;
        internal vxl_rend2d_cam_d Rend2DCam;
        internal vxl_rend2d_d Rend2D;
        internal vxl_uninit_d Uninit;
        internal vxl_drawvox_d DrawVox;
        internal vxl_drawvox_batch_d DrawVox_Batch;
        internal vxl_drawlin_d DrawLine;
        internal vxl_drawsph_d DrawSphere;
        internal vxl_drawpol_d DrawPolygon;
        internal vxl_drawspr_d DrawSprite;
        internal vxl_drawspr_getext_d DrawSprite_GetExtends;
        internal vxl_drawzip_d DrawZip;
        internal vxl_printalph_d DrawTxt;
        internal vxl_pdrawvox_d DrawPVox;
        internal vxl_pdrawbox_d DrawPBox;
        internal vxl_pdrawtxt_d DrawPTxt;
        internal vxl_setrpm_d vxl_setrpm;
        internal vxl_setvxmodel_d? vxl_setvxmodel;
        internal vxl_getrpm_d vxl_getrpm;
        internal vxl_nav_read_d NavRead;
        internal vxl_joy_read_d JoyRead;
        internal vxl_xbox_write_d JoyVibrate;
        internal vxl_vpen_read_d VPenRead;
        #endregion
        #region mapDelegates
        void LedHostSetupDelegates(IntPtr vxlHandle)
        {
            if (AreDelegatesMapped) return;

            // Define the function names and their corresponding delegate types; we give rename the delegates to make them more readable 
            var functionMap = new List<VoxonFunctionMapping>
            {
                new VoxonFunctionMapping("vxl_init", typeof(vxl_init_d), d => Init = (vxl_init_d)d),
                new VoxonFunctionMapping("vxl_getver", typeof(vxl_getver_d), d => GetVer = (vxl_getver_d)d),
                new VoxonFunctionMapping("vxl_loadini", typeof(vxl_loadini_d), d => vxl_loadini = (vxl_loadini_d)d),
                new VoxonFunctionMapping("vxl_frame_start", typeof(vxl_frame_start_d), d => vxl_frame_start = (vxl_frame_start_d)d),
                new VoxonFunctionMapping("vxl_frame_end", typeof(vxl_frame_end_d), d => FrameEnd = (vxl_frame_end_d)d),
                new VoxonFunctionMapping("vxl_saveply", typeof(vxl_saveply_d), d => SavePly = (vxl_saveply_d)d),
                new VoxonFunctionMapping("vxl_rend2d_cam", typeof(vxl_rend2d_cam_d), d => Rend2DCam = (vxl_rend2d_cam_d)d),
                new VoxonFunctionMapping("vxl_rend2d", typeof(vxl_rend2d_d), d => Rend2D = (vxl_rend2d_d)d),
                new VoxonFunctionMapping("vxl_drawvox", typeof(vxl_drawvox_d), d => DrawVox = (vxl_drawvox_d)d),
                new VoxonFunctionMapping("vxl_drawvox_batch", typeof(vxl_drawvox_batch_d), d => DrawVox_Batch = (vxl_drawvox_batch_d)d),
                new VoxonFunctionMapping("vxl_drawlin", typeof(vxl_drawlin_d), d => DrawLine = (vxl_drawlin_d)d),
                new VoxonFunctionMapping("vxl_drawsph", typeof(vxl_drawsph_d), d => DrawSphere = (vxl_drawsph_d)d),
                new VoxonFunctionMapping("vxl_drawpol", typeof(vxl_drawpol_d), d => DrawPolygon = (vxl_drawpol_d)d),
                new VoxonFunctionMapping("vxl_drawspr", typeof(vxl_drawspr_d), d => DrawSprite = (vxl_drawspr_d)d),
                new VoxonFunctionMapping("vxl_drawspr_getext", typeof(vxl_drawspr_getext_d), d => DrawSprite_GetExtends = (vxl_drawspr_getext_d)d),
                new VoxonFunctionMapping("vxl_drawzip", typeof(vxl_drawzip_d), d => DrawZip = (vxl_drawzip_d)d),
                new VoxonFunctionMapping("vxl_printalph", typeof(vxl_printalph_d), d => DrawTxt = (vxl_printalph_d)d),
                new VoxonFunctionMapping("vxl_pdrawvox", typeof(vxl_pdrawvox_d), d => DrawPVox = (vxl_pdrawvox_d)d),
                new VoxonFunctionMapping("vxl_pdrawbox", typeof(vxl_pdrawbox_d), d => DrawPBox = (vxl_pdrawbox_d)d),
                new VoxonFunctionMapping("vxl_pdrawtxt", typeof(vxl_pdrawtxt_d), d => DrawPTxt = (vxl_pdrawtxt_d)d),
                new VoxonFunctionMapping("vxl_setrpm", typeof(vxl_setrpm_d), d => vxl_setrpm = (vxl_setrpm_d)d),
                new VoxonFunctionMapping("vxl_setvxmodel", typeof(vxl_setvxmodel_d), d => vxl_setvxmodel = (vxl_setvxmodel_d)d),
                new VoxonFunctionMapping("vxl_getrpm", typeof(vxl_getrpm_d), d => vxl_getrpm = (vxl_getrpm_d)d),
                new VoxonFunctionMapping("vxl_nav_read", typeof(vxl_nav_read_d), d => NavRead = (vxl_nav_read_d)d),
                new VoxonFunctionMapping("vxl_joy_read", typeof(vxl_joy_read_d), d => JoyRead = (vxl_joy_read_d)d),
                new VoxonFunctionMapping("vxl_xbox_write", typeof(vxl_xbox_write_d), d => JoyVibrate = (vxl_xbox_write_d)d),
                new VoxonFunctionMapping("vxl_vpen_read", typeof(vxl_vpen_read_d), d => VPenRead = (vxl_vpen_read_d)d),
                new VoxonFunctionMapping("vxl_uninit", typeof(vxl_uninit_d), d => Uninit = (vxl_uninit_d)d),
            };

            // Iterate through the function map and map the delegates
            foreach (var mapping in functionMap)
            {
                IntPtr funcaddr = NativeMethods.GetProcAddress(vxlHandle, mapping.FunctionName);
                if (funcaddr == IntPtr.Zero)
                {
                    delegateMappingStatus[mapping.FunctionName] = false;
                    continue;
                }

                Delegate del = Marshal.GetDelegateForFunctionPointer(funcaddr, mapping.DelegateType);
                mapping.AssignDelegate(del);
                delegateMappingStatus[mapping.FunctionName] = true;
            }

            AreDelegatesMapped = true; // Mark as successfully mapped
            
        }

        public Dictionary<string, bool> GetDelegateStatus()
        {
            return new Dictionary<string, bool>(delegateMappingStatus);
        }
        #endregion
        /// <summary>
        /// Loads In the LedHost.DLL and returns a handle
        /// </summary>
        /// <param name="vxlHandle"></param>
        /// <param name="vs"></param>
        /// <returns></returns>
        /// <exception cref="DllNotFoundException"></exception>
        /// <exception cref="FileNotFoundException"></exception>
        public IntPtr LoadLedHostCS(string ledHostDLLPath)
        {
            LedHostDLLPath = Path.GetFullPath(ledHostDLLPath);

            LedHostHandle = NativeMethods.LoadLibrary(ledHostDLLPath);

            if (LedHostHandle == IntPtr.Zero)
            {
                throw new DllNotFoundException("The specified DLL 'LedHost.dll' could not be found.");
                return IntPtr.Zero;
            }

            this.LedHostSetupDelegates(LedHostHandle);

            return LedHostHandle;
        }

        public bool InitaliseLedHostCS(ref vxl_state_t vs)
        {
            LedHostIniInitialised = this.LoadIni(ref vs);

            if (LedHostIniInitialised < -1)
            {
                throw new FileNotFoundException("Ledhost.ini was not found or could not be loaded.");
            }

            this.Init(ref vs);
            return true;

        }

        public string GetLedHostDLLPath()
        {
            return LedHostDLLPath;
        }
        public IntPtr GetLedHostCSHandle()
        {
            return LedHostHandle;
        }


        public bool UnLoadLedHostCS(ref vxl_state_t vs)
        {
            try
            {
                // Close LedHost
                this.Uninit();
                LedhostHasUninitalised = true;

                // Check if the handle is valid before attempting to free the library
                if (LedHostHandle != IntPtr.Zero)
                {
                    // Attempt to free the library
                    if (!NativeMethods.FreeLibrary(LedHostHandle))
                    {
                        throw new InvalidOperationException("Failed to free the LedHost library.");
                    }

                    LedHostHandle = IntPtr.Zero;
                }
                else
                {
                    throw new InvalidOperationException("Invalid LedHost handle. Unable to free library.");
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error occurred: {ex.Message}");
                // Optionally, log the exception or perform other error handling
                return false;
            }
        }

        ~LedHostCS()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {

            if (LedHostHandle != IntPtr.Zero)
            {

                if (LedhostHasUninitalised == false)
                {
                    //  vxl_state_t dummyvs = new vxl_state_t();
                    this.Uninit();
                }


                // Attempt to free the library
                if (!NativeMethods.FreeLibrary(LedHostHandle))
                {
                    throw new InvalidOperationException("Failed to free the LedHost library.");
                }

                LedHostHandle = IntPtr.Zero;
            }

        }


        // Test function to check if the LedHost.dll is loaded and working
        public static bool Test() // Min version
        {

            vxl_state_t vs = new vxl_state_t();
            LedHostCS ledHost = new LedHostCS();
            string LedHostPath = VLED_CS_Utils.GetVLEDFilePath("LedHost.dll");

            IntPtr LHHandle = ledHost.LoadLedHostCS(LedHostPath);

            ledHost.InitaliseLedHostCS(ref vs);
            Console.WriteLine($"LEDHOST version :{ledHost.GetVer()}");

            ledHost.UnLoadLedHostCS(ref vs);

            return true;
        }

        // Helper Functions --

        public int LoadIni(ref vxl_state_t vs)
        {
            if (vxl_loadini == null)
            {
                throw new InvalidOperationException("vxl_loadini delegate is not loaded.");
            }

            int returnValue = vxl_loadini(ref vs);
            intendedRPM = Math.Abs(vs.rpm);
            return returnValue;
        }

        public void FrameStart(ref vxl_state_t vs)
        {
            if (vxl_frame_start == null)
            {
                throw new InvalidOperationException("vxl_frame_start delegate is not loaded.");
            }
            vxl_frame_start(ref vs);
            _DrawBorder(ref vs);
        }

        public void SetRPM(ref vxl_state_t vs, int newRPMValue)
        {
            if (vxl_setrpm == null)
            {
                throw new InvalidOperationException("vxl_setrpm delegate is not loaded.");
            }
            vxl_setrpm(ref vs, newRPMValue);
            // If newrpm is greater than zero, isMotorRunning will be true; otherwise, false.
            isMotorRunning = newRPMValue > 0;
        }

        // Switch the simulated Voxon model (VX2 = 2, VX2-XL = 1).  Safe no-op if
        // the running ledhost.dll doesn't export vxl_setvxmodel.
        public bool SetVxModel(ref vxl_state_t vs, int model)
        {
            if (vxl_setvxmodel == null) return false;
            vxl_setvxmodel(ref vs, model);
            return true;
        }

        public int GetRPM(ref vxl_state_t vs)
        {
            if (vxl_getrpm == null)
            {
                throw new InvalidOperationException("vxl_getrpm delegate is not loaded.");
            }
            return vxl_getrpm(ref vs);
        }


        public point3d SetAspectRatio(ref vxl_state_t vs)
        {
            point3d result;
            result.x = GetAspectRatioX(ref vs);
            result.y = GetAspectRatioY(ref vs);
            result.z = GetAspectRatioZ(ref vs);
            return result;
        }

        public float GetAspectRatioX(ref vxl_state_t vs)
        {
            return (float)vs.xsiz / 64;
        }

        public float GetAspectRatioY(ref vxl_state_t vs)
        {
            return (float)vs.ysiz / 64;
        }

        // Note Aspect Ratio Z is the same as X
        public float GetAspectRatioZ(ref vxl_state_t vs)
        {

            return (float)vs.xsiz / 64;
        }

        // works out how many Z slices per degree
        public float GetSlicePerDegree(ref vxl_state_t vs)
        {
            return (float)vs.zsiz / 360f;
        }
        float GetSlicePerRadian(ref vxl_state_t vs)
        {
            return (float)vs.zsiz / (2 * 3.14159265358979323846f);
        }


        // Ensure the position is within the bounds of the volume
        public void ClampWithinVolume(ref vxl_state_t vs, ref point3d pos)
        {
            float radius = GetAspectRatioX(ref vs);

            if (pos.z > GetAspectRatioZ(ref vs)) pos.z = GetAspectRatioZ(ref vs);
            if (pos.z < -GetAspectRatioZ(ref vs)) pos.z = -GetAspectRatioZ(ref vs);

            float distSquared = pos.x * pos.x + pos.y * pos.y;
            if (distSquared > radius * radius)
            {
                float scale = radius / MathF.Sqrt(distSquared);
                pos.x *= scale;
                pos.y *= scale;
            }

        }

        public void RotVex(float angInRadians, ref point3d a, ref point3d b)
        {
            float f, c, s;

            c = MathF.Cos(angInRadians);
            s = MathF.Sin(angInRadians);
            f = a.x;
            a.x = f * c + b.x * s;
            b.x = b.x * c - f * s;
            f = a.y;
            a.y = f * c + b.y * s;
            b.y = b.y * c - f * s;
            f = a.z;
            a.z = f * c + b.z * s;
            b.z = b.z * c - f * s;
        }

        //! Renders a 2D textured quad (plane) onto the volumetric display. Useful for rendering 2D textures. Must be called between startFrame() & endFrame() functions.
        /**
         * @param vs            Pointer to the voxel state struct (vxl_state_t).
         * @param texture       Filename or path for the texture to load. Must be a tile type; null for no texture.
         * @param pos           Center position of the quad to render.
         * @param width         X dimension of the quad (width).
         * @param height        Y dimension of the quad (height).
         * @param yawDegrees    Horizontal angle (yaw). 0 is front-facing, 180 is back-facing. Presented in degrees.
         * @param pitchDegrees  Vertical angle (pitch). 0 is horizontal, 90 is vertical. Presented in degrees.
         * @param rollDegrees   Twist angle (roll). 0 is flat. Presented in degrees.
         * @param hexColour     Color value of the texture. 0x404040 is the natural color; other values add a tint.
         * @param u             U value of the texture. Adjusting this stretches the horizontal texture size. Default is 1.
         * @param v             V value of the texture. Adjusting this stretches the vertical texture size. Default is 1.
         *
         * @returns 1 if texture not found or issue with tile type; 0 if successful.
         * Must be called between startFrame() & endFrame() functions.
         */
        int DrawQuad(ref vxl_state_t vs, ref tiletype texture, ref point3d pos, float width, float height,
            float yawDegrees, float pitchDegrees, float rollDegrees, int hexColour, float uValue, float vValue)
        {
            int returnValue = 0;
            poltex[] vt = new poltex[4];
            point3d vx, vy;
            float f, g;
            float PI = 3.14159265358979323846f;


            // convert values into radians...
            float hAngRadians = yawDegrees *= PI / 180.0f;
            float vAngRadians = pitchDegrees *= PI / 180.0f;
            float twistRadians = rollDegrees *= PI / 180.0f;

            vx.x = MathF.Cos(hAngRadians);
            vx.y = MathF.Sin(hAngRadians);
            vx.z = 0.0f;

            f = MathF.Cos(vAngRadians);
            vy.x = -vx.y * f;
            vy.y = vx.x * f;
            vy.z = -MathF.Sin(vAngRadians);

            RotVex(twistRadians, ref vx, ref vy);

            //	vxl_drawsph(vs, pos->x, pos->y, pos->z, .10, 0, 0x00ff00); // for debugging
            for (int i = 0; i < 4; i++)
            {
                if (((i + 1) & 2) != 0) { f = -width; vt[i].u = 0.0f; }
                else { f = width; vt[i].u = uValue; }
                if ((i & 2) == 0) { g = -height; vt[i].v = 0.0f; }
                else { g = height; vt[i].v = vValue; }

                vt[i].x = vx.x * f + vy.x * g + pos.x;
                vt[i].y = vx.y * f + vy.y * g + pos.y;
                vt[i].z = vx.z * f + vy.z * g + pos.z;
                vt[i].col = 0;
                //	vxl_drawsph(vs, vt[i].x, vt[i].y, vt[i].z, .10, 0, cols[i]); // for debugging
            }

            if (texture.first_pixel == texture.pitch && texture.pitch == texture.width && texture.width == texture.height)
            {
                returnValue = 1; // tiletype is has not been loaded with any image data, defaulting to colour draw
                texture = new tiletype();
            }

            DrawPolygon(ref vs, ref texture, vt, 4, hexColour);

            return returnValue;
        }
        public bool IsMotorRunning()
        {
            return isMotorRunning;
        }

        public void ToggleMotor(ref vxl_state_t vs)
        {

            if (isMotorRunning)
            {
                SetRPM(ref vs, 0);
            }
            else
            {
                SetRPM(ref vs, intendedRPM);
            }

        }
        public int GetIntendedRPM()
        {
            return intendedRPM;
        }
        public void SetIntendedRPM(int newRPM)
        {
            intendedRPM = newRPM;
        }


        // Private Functions 
        private void _DrawBorder(ref vxl_state_t vs)
        {
            if (vs.drawbord == 0) return;

            int m = 0, x = 0, y = 0, z = 0, i = 0, j = 0;

            //top&bot rings
            for (z = 0; z < vs.zsiz; z++)
                for (y = 0; y < vs.ysiz; y += vs.ysiz - 1)
                    for (x = 0; x < vs.xsiz; x += vs.xsiz - 1)
                        DrawPVox(ref vs, x, y, z, 7);

            //wall zigzags
            m = ((vs.xsiz * vs.xsiz) >> 14);
            for (z = 0; z < vs.zsiz; z += 128 * m)
                for (y = 0; y < vs.ysiz; y++)
                    for (i = -1; i <= 1; i += 2)
                        DrawPVox(ref vs, 0, y, y * i * m + z, 7);
        }


    }
}
