using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Voxon
{
    // Cross-thread bridge: Avalonia UI (main thread) <-> game loop (bg STA
    // thread).  All fields are volatile; one-shot commands are self-clearing
    // flags.  No locks on the hot path.  This is reusable boilerplate - add
    // your own game fields in the "game" section and leave the rest alone.
    internal static class Bridge
    {
        // ---- universal visual controls -------------------------------
        public static volatile float Density = 2.0f;       // voxel DPI
        public static volatile float FigureDensity = 3.0f; // spare density knob
        public static volatile int   Color = 0x20E0C0;     // accent colour 0xRRGGBB
        public static volatile bool  DemoMode = false;      // attract mode

        // ---- placeholder GAME controls (replace per game) ------------
        public static volatile float RotSpeed = 0.5f;       // demo word spin
        public static volatile float TextSize = 1.0f;       // demo word scale
        public static volatile bool  ShowTitle = false;     // classic HELLO VOXEL text vs the lighting bench

        // ---- lighting (universal, applied in Lighting.cs) ------------
        public static volatile bool  UseGpu = true;         // GPU (ComputeSharp/DX12) vs CPU (SIMD) lighting
        public static volatile bool  GpuActive = false;     // status: GPU path actually ran (game -> UI)
        public static volatile int   LightMode = 2;         // 0 Flat 1 Normals 2 Spotlight
        public static volatile float AmbientBright = 0.15f; // ambient floor
        public static volatile float Exposure = 1.0f;       // global brightness
        public static volatile float SelfIllum = 1.0f;      // (unused; kept for save-file compat)
        public static volatile float SpotIntensity = 8.0f;  // main spotlight intensity (0..10)
        public static volatile float SpotRadius = 4.0f;     // spotlight falloff radius
        public static volatile bool  Shadows = true;        // spotlight shadow maps
        public static volatile float ShadowStrength = 0.75f;// (unused; kept for save-file compat)
        public static volatile float ShadowBias = 0.02f;    // world-space self-shadow bias
        public static volatile bool  OrbitLights = true;    // sweep the main light so shadows move
        public static volatile float NormalStrength = 1.0f; // normals mode: 0 flat .. 1 full N.L
        public static volatile float NormalIntensity = 1.0f;// normals mode overall multiplier
        public static volatile float LightAngle = 45f;      // normals mode light azimuth (deg)

        // ---- hardware / VLED image controls (Hardware tab) -----------
        public static volatile int  System = 0;             // 0 = VX2, 1 = VX2-XL (set from the DLL, not the UI)
        public static volatile bool SystemDirty = true;
        public static volatile bool MotorOn = true;
        public static volatile int  Rpm = 900;
        public static volatile int  MaxRpm = 900;           // rpm ceiling published by the DLL (vs.maxrpm)
        public static volatile int  DrawBilin = 0;          // 0 nearest, 1 bilinear
        public static volatile int  DithMode = 0;           // 0 diffuse, 1 ordered
        public static volatile int  DithThresh = 64;        // 0..255
        public static volatile bool DrawBorder = false;
        public static volatile float Gamma = 2.0f;          // 0.25..4.25

        // ---- emulator camera (additive deltas, cleared by game thread) -
        public static volatile float CamDH, CamDV, CamDZoom;
        public static volatile bool CamReset;
        public static volatile bool CamHomeSet;
        public static float CamHomeH, CamHomeV, CamHomeD;

        // ---- one-shot commands ---------------------------------------
        public static volatile bool RegenRequested, RandomizeRequested, NewGameRequested, PauseToggleRequested, Quit;
        public static volatile bool RecordStartRequested;    // Ctrl+R start (UI resolves the folder first)
        public static volatile bool RecordStopRequested;     // Ctrl+R stop
        public static volatile string RecordDir = "";        // folder chosen by the UI thread
        public static volatile string RecordPath = "";       // current / last recording file (game -> UI)
        public static volatile bool Recording;               // status (game -> UI)
        public static volatile int Want = -1;   // queued Dir6 (-1 = none)

        // ---- audio ----------------------------------------------------
        public static volatile bool SoundEnabled = true;
        public static volatile float Volume = 0.7f;

        // ---- persistence ---------------------------------------------
        public static volatile bool SaveRequested;
        public static volatile int[] HighScores = Array.Empty<int>();

        // ---- status (game -> UI) -------------------------------------
        public static volatile int Score, HiScore, Level, Lives, Vps, Voxels;
        public static volatile bool HardwareFailed, HardwareLive;
        public static string BannerText = "", HintText = "";

        // published LedWin native window handle (0 until created)
        private static long _hwnd;
        public static long LedWinHwnd { get => Interlocked.Read(ref _hwnd); set => Interlocked.Exchange(ref _hwnd, value); }
    }

    // Which Voxon display are we driving?  Reusable boilerplate.
    //
    // IMPORTANT: vxl_state_t carries NO device-id field.  vs.vxmodeln looks
    // like one but the SDK documents it as "the maximum index (+1) that can be
    // sent to vxl_setvxmodel()" - a COUNT (measured: 4).  Comparing it against
    // the ledhost.ini model codes (VX2 0x43 / VX2-XL 0x44) can never match.
    // What the DLL *does* publish per model is the volume itself, so identify
    // the device from that:
    //
    //     VX2     boundr 2  (boundr2 4)   xsiz 128   maxrpm 900
    //     VX2-XL  boundr 4  (boundr2 16)  xsiz 256   maxrpm 600
    //
    // Note boundr2 is r-SQUARED, not a second radius - reading it as one is
    // what makes a plain VX2 look like an XL.
    internal static class VoxDevice
    {
        public const float VX2BoundR = 2f;
        public const float XLBoundR  = 4f;
        public const int   VX2MaxRpm = 900;
        public const int   XLMaxRpm  = 600;

        const float XLBoundRMin = 3f;   // midpoint between the two radii

        public static bool IsXL(float boundr) => boundr >= XLBoundRMin;

        public static bool IsXL(ref vxl_state_t vs)
            => vs.boundr > 0.5f ? IsXL(vs.boundr)   // bounds are authoritative
                                : vs.xsiz >= 256;   // fallback: LED grid width
    }

    // Drives the physical Voxon volume + the LedWin emulator from the game
    // model.  Runs on its own background thread.  Reusable boilerplate.
    internal static class GameHost
    {
        // unique title so the UI can locate the window to embed it
        public const string LedWinTitle = "HelloVoxel_LedWin";

        [DllImport("user32", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string? cls, string? title);

        public static void RunGame()
        {
            var vs = new vxl_state_t();
            var host = new LedHostCS();
            try
            {
                host.LoadLedHostCS(VLED_CS_Utils.GetVLEDFilePath("ledhost.dll"));
                host.InitaliseLedHostCS(ref vs);
            }
            catch (Exception e)
            {
                Console.WriteLine("[game] LedHost init failed: " + e.Message);
                Bridge.HardwareFailed = true;
            }

            var win = new LedWinCS();
            if (!win.LedWinInit(LedWinTitle, 900, 720, 220, 120))
            {
                Console.Error.WriteLine("[game] LedWin init failed (check LedWin.dll is present)");
                Bridge.HardwareFailed = true;
                Bridge.Quit = true;
                return;
            }

            if (!host.IsMotorRunning()) { try { host.ToggleMotor(ref vs); } catch { } }
            // never exceed the ceiling the DLL reports for the current model
            try { host.SetRPM(ref vs, vs.maxrpm > 0 ? Math.Min(Bridge.Rpm, vs.maxrpm) : Bridge.Rpm); } catch { }

            // publish the LedWin window handle for embedding in Avalonia
            try { Bridge.LedWinHwnd = FindWindow(null, LedWinTitle).ToInt64(); } catch { }

            // remember the default emulator camera for the Reset View button
            try { Bridge.CamHomeH = win.GetEmuHAng(); Bridge.CamHomeV = win.GetEmuVAng(); Bridge.CamHomeD = win.GetEmuDist(); Bridge.CamHomeSet = true; } catch { }

            ApplySystemBounds(ref vs, host);
            GameModel.Init();
            Audio.Start();

            while (win.Breath() == 0 && !Bridge.Quit)
            {
                ProcessBridge();
                ApplyHardware(ref vs, host);
                PollGamepad(win);
                PollSpaceMouse(host, win);

                float dt = win.GetDeltaTime();
                if (dt > 0.1f) dt = 0.1f;
                Bridge.Vps = dt > 0 ? (int)(1f / dt) : 0;

                ApplyCamera(win);

                GameModel.Tick(dt);

                // Lighting + transform: the static model is lit on the GPU
                // (spin applied there) into world-space Out* buffers.
                Lighting.UpdateLights(GameModel.Clock);
                Lighting.RenderFrame();

                // Everything is voxels -> a single batch call (no DrawLine/DrawPTxt).
                host.FrameStart(ref vs);
                int cnt = GameModel.OutCount;
                if (cnt > 0)
                    host.DrawVox_Batch(ref vs, ref GameModel.OutX[0], ref GameModel.OutY[0], ref GameModel.OutZ[0], ref GameModel.OutC[0], cnt, 0);
                host.FrameEnd(ref vs);

                // depth-map recording (Ctrl+R): capture the world-space voxels
                if (DepthRecorder.IsRecording && cnt > 0)
                    DepthRecorder.Capture(GameModel.OutX, GameModel.OutY, GameModel.OutZ, GameModel.OutC, cnt);
                win.Render(ref host, ref vs);

                PublishStatus();
            }

            Bridge.Quit = true;
            try { DepthRecorder.Stop(); } catch { }   // finalise any in-progress recording
            try
            {
                host.ToggleMotor(ref vs);
                host.UnLoadLedHostCS(ref vs);
                host.Dispose();
                win.UninitWindow();
                win.Dispose();
            }
            catch { }
        }

        // Xbox controller: left stick X = spin CW/CCW, left stick Y = in/out,
        // right stick Y = up/down.  Re-map for your game in GameModel.SetWant.
        static void PollGamepad(LedWinCS win)
        {
            try
            {
                if (win.GetJoyCount() <= 0) return;
                float lx = win.GetJoyAxisValue(0, VX_JOY_AXIS_CODES.JOY_AXIS_LEFT_STICK_X);
                float ly = win.GetJoyAxisValue(0, VX_JOY_AXIS_CODES.JOY_AXIS_LEFT_STICK_Y);
                float ry = win.GetJoyAxisValue(0, VX_JOY_AXIS_CODES.JOY_AXIS_RIGHT_STICK_Y);
                const float TH = 0.5f;
                Dir6 want = Dir6.None;
                if (MathF.Abs(lx) > TH && MathF.Abs(lx) >= MathF.Abs(ly))
                    want = lx > 0 ? Dir6.AngCW : Dir6.AngCCW;
                else if (MathF.Abs(ly) > TH)
                    want = ly < 0 ? Dir6.RadOut : Dir6.RadIn;
                if (MathF.Abs(ry) > TH)
                    want = ry < 0 ? Dir6.VertUp : Dir6.VertDown;
                if (want != Dir6.None) GameModel.SetWant(want);
            }
            catch { }
        }

        static void ProcessBridge()
        {
            if (Bridge.SaveRequested) { Bridge.SaveRequested = false; Persist.Save(); }

            int w = Bridge.Want;
            if (w >= 0) { GameModel.SetWant((Dir6)w); Bridge.Want = -1; }

            if (Bridge.NewGameRequested) { Bridge.NewGameRequested = false; GameModel.RequestNewGame(); }
            if (Bridge.RandomizeRequested) { Bridge.RandomizeRequested = false; GameModel.Randomize(); }
            if (Bridge.RegenRequested) { Bridge.RegenRequested = false; GameModel.Regenerate(0, 0, 0); }
            if (Bridge.PauseToggleRequested) { Bridge.PauseToggleRequested = false; GameModel.TogglePause(); }
            if (Bridge.RecordStopRequested)
            {
                Bridge.RecordStopRequested = false;
                DepthRecorder.Stop();
                Bridge.Recording = DepthRecorder.IsRecording;
            }
            if (Bridge.RecordStartRequested)
            {
                Bridge.RecordStartRequested = false;
                if (!DepthRecorder.IsRecording)
                    DepthRecorder.Start(GameModel.BOUNDR, GameModel.BOUNDZ, Bridge.RecordDir);
                Bridge.RecordPath = DepthRecorder.LastPath;
                Bridge.Recording = DepthRecorder.IsRecording;
            }

            GameModel.Density = Bridge.Density;
            GameModel.FigureDensity = Bridge.FigureDensity;
            GameModel.Color = Bridge.Color;
            GameModel.DemoMode = Bridge.DemoMode;
            GameModel.RotSpeed = Bridge.RotSpeed;
            GameModel.TextSize = Bridge.TextSize;
            GameModel.ShowTitle = Bridge.ShowTitle;

            // lighting coordinator (Lighting.cs) - copy the live controls across
            Lighting.UseGpu = Bridge.UseGpu;
            Lighting.Mode = (LightMode)Math.Clamp(Bridge.LightMode, 0, 2);   // Flat / Normals / Spotlight
            Lighting.Ambient = Bridge.AmbientBright;
            Lighting.Brightness = Bridge.Exposure;
            Lighting.SpotIntensity = Bridge.SpotIntensity;
            Lighting.SpotRadius = Bridge.SpotRadius;
            Lighting.ShadowBias = Bridge.ShadowBias;
            Lighting.OrbitLights = Bridge.OrbitLights;
            Lighting.NormalStrength = Bridge.NormalStrength;
            Lighting.NormalIntensity = Bridge.NormalIntensity;
            Lighting.LightAngleDeg = Bridge.LightAngle;

            Audio.Enabled = Bridge.SoundEnabled;
            Audio.Volume = Bridge.Volume;
        }

        // Apply accumulated mouse/wheel/button deltas to the emulator camera.
        static void ApplyCamera(LedWinCS win)
        {
            if (Bridge.CamReset)
            {
                Bridge.CamReset = false;
                if (Bridge.CamHomeSet) { try { win.SetEmuPosition(Bridge.CamHomeH, Bridge.CamHomeV, Bridge.CamHomeD); } catch { } }
                return;
            }
            float dh = Bridge.CamDH, dv = Bridge.CamDV, dz = Bridge.CamDZoom;
            if (dh == 0f && dv == 0f && dz == 0f) return;
            Bridge.CamDH = 0f; Bridge.CamDV = 0f; Bridge.CamDZoom = 0f;
            try
            {
                float h = win.GetEmuHAng() + dh;
                float v = win.GetEmuVAng() + dv;
                float d = win.GetEmuDist() * MathF.Pow(0.9f, dz);
                if (d < 0.01f) d = 0.01f;
                win.SetEmuPosition(h, v, d);
            }
            catch { }
        }

        private static int _lastRpm = -1;
        private static vxl_nav_t Nav = new vxl_nav_t();

        // ---- resolving the simulated model index -------------------------
        // vxl_setvxmodel() takes an INDEX into the DLL's internal model table,
        // not a model id, and that table is runtime-specific.  The shipped
        // ledhost.txt documents "0=VX2 (old), 1=VX2 (new), 2=VX2-XL", but
        // runtime 20260617 actually reports 0=VX2 old, 1=XL old, 2=VX2 new,
        // 3=XL new (vxl_init lands on 2, matching ledhost.ini vxmodeldef=0x43).
        // Hardcoding an index is therefore wrong on some runtime; instead walk
        // the table once and let the DLL tell us which index is which device.
        static int _simVX2 = -1, _simXL = -1;
        static bool _simResolved;

        // last volume the DLL reported, so a change can be detected each frame
        static bool _lastHwLive;
        static float _lastBoundR = -1f, _lastBoundZ = -1f;
        static int _lastMaxRpm = -1;

        static void ResolveSimModels(ref vxl_state_t vs, LedHostCS host)
        {
            if (_simResolved) return;
            _simResolved = true;

            int n = vs.vxmodeln;      // COUNT (max index + 1), NOT a device id
            if (n <= 0 || n > 64) return;   // implausible -> leave -1, never switch
            for (int i = 0; i < n; i++)
            {
                try { if (!host.SetVxModel(ref vs, i)) return; } catch { return; }
                // keep the LAST match of each kind: later entries are the newer revisions
                if (VoxDevice.IsXL(ref vs)) _simXL = i; else _simVX2 = i;
            }
        }

        // Fit the app to the display volume.
        //
        //  * REAL HARDWARE (vs.flags reports FT60x / VSPIN): the DLL owns the
        //    volume, full stop.  Bounds, rpm ceiling and model all come from
        //    the values it returns; the app never infers a resolution from its
        //    own constants and the UI selector is locked.  If the DLL hasn't
        //    populated a value we keep the last good one and say so, because
        //    substituting a nominal VX2/VX2-XL volume would be a guess about
        //    the machine on the end of the cable.
        //  * SIMULATOR (nothing connected): the user is free to switch the
        //    simulated model, so ask the DLL to switch via vxl_setvxmodel using
        //    the index the DLL itself reported, then read the volume back out.
        //    Only here may nominal constants stand in, and only if the DLL
        //    returned nothing usable.
        static bool _boundsWarned;

        static void ApplySystemBounds(ref vxl_state_t vs, LedHostCS host)
        {
            bool hwLive = (vs.flags & 3) != 0;   // VSFLAGS_GOTFT60X | VSFLAGS_GOTVSPIN
            Bridge.HardwareLive = hwLive;

            if (!hwLive)
            {
                ResolveSimModels(ref vs, host);
                int idx = Bridge.System == 0 ? _simVX2 : _simXL;
                if (idx >= 0) { try { host.SetVxModel(ref vs, idx); } catch { } }
            }

            if (vs.boundr > 0.05f && vs.boundz > 0.05f)
            {
                // The DLL told us the volume - use it verbatim, whatever it is.
                GameModel.SetBounds(vs.boundr, vs.boundz);
                if (vs.maxrpm > 0) Bridge.MaxRpm = vs.maxrpm;
                Bridge.System = VoxDevice.IsXL(ref vs) ? 1 : 0;   // label only; nothing above derives from it
            }
            else if (hwLive)
            {
                // Hardware is up but the DLL reported no bounds.  Keep the last
                // good volume rather than inventing one.
                if (!_boundsWarned)
                {
                    _boundsWarned = true;
                    Console.Error.WriteLine($"[game] hardware live but ledhost reported no bounds "
                        + $"(boundr={vs.boundr}, boundz={vs.boundz}) - keeping {GameModel.BOUNDR} x {GameModel.BOUNDZ}");
                }
            }
            else
            {
                // Simulator and the model switch didn't take: fall back to the
                // nominal volume for the model the user asked for.
                bool xl = Bridge.System == 1;
                GameModel.SetBounds(xl ? VoxDevice.XLBoundR : VoxDevice.VX2BoundR, 2f);
                Bridge.MaxRpm = xl ? VoxDevice.XLMaxRpm : VoxDevice.VX2MaxRpm;
            }

            // snapshot what the DLL is reporting so ApplyHardware can spot changes
            _lastHwLive = hwLive; _lastBoundR = vs.boundr; _lastBoundZ = vs.boundz; _lastMaxRpm = vs.maxrpm;
        }

        // 3Dconnexion SpaceMouse: push a world direction to the game via the
        // LedHost SpaceNav bindings (absolute control).
        static void PollSpaceMouse(LedHostCS host, LedWinCS win)
        {
            try
            {
                if (host.NavRead(0, ref Nav) != 1) return;
                win.ReplaceNavInputStruct(0, Nav);
                float nx = win.GetNavAxisValue(0, VX_NAV_AXIS_CODES.NAV_X_AXIS_DIRECTION);
                float ny = win.GetNavAxisValue(0, VX_NAV_AXIS_CODES.NAV_Y_AXIS_DIRECTION);
                float nz = win.GetNavAxisValue(0, VX_NAV_AXIS_CODES.NAV_Z_AXIS_DIRECTION);
                if (nx * nx + ny * ny + nz * nz < 0.16f) return;   // deadzone
                GameModel.SetWantWorld(nx, ny, nz);
            }
            catch { }
        }

        // Push the Hardware-tab settings onto the live vxl_state + device.
        static void ApplyHardware(ref vxl_state_t vs, LedHostCS host)
        {
            vs.drawbilin = Bridge.DrawBilin;
            vs.dithmode = Bridge.DithMode;
            vs.dithresh = Math.Clamp(Bridge.DithThresh, 0, 255);
            vs.drawbord = Bridge.DrawBorder ? 1 : 0;
            vs.gammapow = Math.Clamp(Bridge.Gamma, 0.25f, 4.25f);

            try { if (host.IsMotorRunning() != Bridge.MotorOn) host.ToggleMotor(ref vs); } catch { }

            bool hwLive = (vs.flags & 3) != 0;
            Bridge.HardwareLive = hwLive;

            // Re-fit whenever the DLL's own numbers move, not just when the user
            // asks: hardware detection can complete after vxl_init (flags flip
            // late), and vxpanel can switch the model out from under us.  Without
            // this the app would keep simulator bounds on a real machine.
            if (Bridge.SystemDirty || hwLive != _lastHwLive
                || vs.boundr != _lastBoundR || vs.boundz != _lastBoundZ || vs.maxrpm != _lastMaxRpm)
            {
                Bridge.SystemDirty = false;
                ApplySystemBounds(ref vs, host);              // (render thread) forwards vxl_setvxmodel
                if (Bridge.Rpm > Bridge.MaxRpm) Bridge.Rpm = Bridge.MaxRpm;   // ceiling from the DLL
            }

            if (Bridge.Rpm != _lastRpm)
            {
                _lastRpm = Bridge.Rpm;
                try { host.SetRPM(ref vs, Bridge.Rpm); } catch { }
            }
        }

        static void PublishStatus()
        {
            Bridge.Score = GameModel.Score;
            Bridge.HiScore = GameModel.HiScore;
            Bridge.Level = GameModel.Level;
            Bridge.Lives = GameModel.Lives;
            Bridge.Voxels = GameModel.VoxelCount;
            Bridge.GpuActive = Lighting.GpuActive;
            Bridge.BannerText = GameModel.Banner;
            Bridge.HintText = GameModel.Hint;
        }
    }
}
