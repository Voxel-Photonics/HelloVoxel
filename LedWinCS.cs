using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

/// LedWinCS is a C# wrapper for the LedWin.dll C++ library.
/// Just updated this to be more similar to LedHostCS - now is dynamically loading

// LedWinCS For Runtime 0.4.7

namespace Voxon
{
    public enum LW_REPORTS
    {
        LW_REPORT_VPS = 0,
        LW_REPORT_VXL_STATE = 1,
        LW_REPORT_SPACE_NAV = 2,
        LW_REPORT_KEYBOARD = 3,
        LW_REPORT_JOYSTICK = 4,
        LW_REPORT_MOUSE = 5,

    };

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

    public class LedWinCS : IDisposable
    {
        private IntPtr ledWinDLLHandle;
        private string ledWinDLLPath;
        public bool AreDelegatesMapped { get; private set; } = false;
        private Dictionary<string, bool> delegateMappingStatus = new Dictionary<string, bool>();


        public int currentWindowPosX;
        public int currentWindowPosY;
        public int currentWindowResX;
        public int currentWindowResY;


        #region Defining Delegates

        private delegate IntPtr CreateLedWinDelegate();
        private CreateLedWinDelegate CreateLedWin;

        private delegate int DeleteLedWinDelegate(IntPtr obj);
        private DeleteLedWinDelegate DeleteLedWin;

        private delegate void LW_QuitLoopDelegate(IntPtr obj);
        private LW_QuitLoopDelegate LW_QuitLoop;

        private delegate int LW_InitAppDelegate(IntPtr obj, int launchFlags);
        private LW_InitAppDelegate LW_InitApp;

        private delegate void LW_UninitAppDelegate(IntPtr obj);
        private LW_UninitAppDelegate LW_UninitApp;

        private delegate ulong LW_GetVersionDelegate(IntPtr obj);
        private LW_GetVersionDelegate LW_GetVersion;

        private delegate IntPtr LW_GetBuildVersionDelegate(IntPtr obj);
        private LW_GetBuildVersionDelegate LW_GetBuildVersion;

        private delegate void LW_SetVXLVersionDelegate(IntPtr obj, long newVXLVer);
        private LW_SetVXLVersionDelegate LW_SetVXLVersion;

        private delegate ref tiletype LW_GetTTDelegate(IntPtr obj);
        private LW_GetTTDelegate LW_GetTT;

        private delegate int LW_BreathDelegate(IntPtr obj);
        private LW_BreathDelegate LW_Breath;

        private delegate void LW_SetFlagsDelegate(IntPtr obj, int newSettingsFlag);
        private LW_SetFlagsDelegate LW_SetFlags;

        private delegate void LW_SetFlagDelegate(IntPtr obj, int value, int flag);
        private LW_SetFlagDelegate LW_SetFlag;

        private delegate bool LW_IsFlagSetDelegate(IntPtr obj, int flag);
        private LW_IsFlagSetDelegate LW_IsFlagSet;

        private delegate int LW_GetFlagsDelegate(IntPtr obj);
        private LW_GetFlagsDelegate LW_GetFlags;

        private delegate void LW_SetWindResDelegate(IntPtr obj, int xres, int yRes);
        private LW_SetWindResDelegate LW_SetWindRes;

        private delegate void LW_MoveWindDelegate(IntPtr obj, int xpos, int ypos);
        private LW_MoveWindDelegate LW_MoveWind;

        private delegate void LW_SetProgNameDelegate(IntPtr obj, [MarshalAs(UnmanagedType.LPStr)] string progname);
        private LW_SetProgNameDelegate LW_SetProgName;

        private delegate int LW_IsWindowFocusedDelegate(IntPtr obj);
        private LW_IsWindowFocusedDelegate LW_IsWindowFocused;

        private delegate void LW_GetWindowSizeInfoDelegate(IntPtr obj, ref int xPos, ref int yPos, ref int xRes, ref int yRes);
        private LW_GetWindowSizeInfoDelegate LW_GetWindowSizeInfo;

        private delegate int LW_StartDirectDrawDelegate(IntPtr obj, ref tiletype tileDD);
        private LW_StartDirectDrawDelegate LW_StartDirectDraw;

        private delegate void LW_StopDirectDrawDelegate(IntPtr obj);
        private LW_StopDirectDrawDelegate LW_StopDirectDraw;

        private delegate void LW_NextPageDelegate(IntPtr obj);
        private LW_NextPageDelegate LW_NextPage;

        private delegate void LW_DrawTxtDelegate(IntPtr obj, int ox, int y, int fcol, int bcol, string progname);
        private LW_DrawTxtDelegate LW_DrawTxt;

        private delegate void LW_DrawPixelDelegate(IntPtr obj, int x, int y, int col);
        private LW_DrawPixelDelegate LW_DrawPixel;

        private delegate void LW_DrawHLineDelegate(IntPtr obj, int x0, int x1, int y, int col);
        private LW_DrawHLineDelegate LW_DrawHLine;

        private delegate void LW_DrawLineDelegate(IntPtr obj, float x0, float y0, float x1, float y1, int col);
        private LW_DrawLineDelegate LW_DrawLine;

        private delegate void LW_DrawCircleDelegate(IntPtr obj, int xc, int yc, int r, int col);
        private LW_DrawCircleDelegate LW_DrawCircle;

        private delegate void LW_DrawRectDelegate(IntPtr obj, int x0, int y0, int x1, int y1, int col);
        private LW_DrawRectDelegate LW_DrawRect;

        private delegate void LW_DrawRectFillDelegate(IntPtr obj, int x0, int y0, int x1, int y1, int col);
        private LW_DrawRectFillDelegate LW_DrawRectFill;

        private delegate void LW_DrawCircleFillDelegate(IntPtr obj, int xc, int yc, int r, int col);
        private LW_DrawCircleFillDelegate LW_DrawCircleFill;

        private delegate void LW_DrawTileDelegate(IntPtr obj, ref tiletype image, int xOffset, int yOffset);
        private LW_DrawTileDelegate LW_DrawTile;

        private delegate int LW_ReportDelegate(IntPtr obj, int reportType, int xPos, int yPos, ref vxl_state_t vs);
        private LW_ReportDelegate LW_Report;

        private delegate float LW_GetEmuHAngDelegate(IntPtr obj);
        private LW_GetEmuHAngDelegate LW_GetEmuHAng;

        private delegate float LW_GetEmuVAngDelegate(IntPtr obj);
        private LW_GetEmuVAngDelegate LW_GetEmuVAng;

        private delegate float LW_GetEmuDistDelegate(IntPtr obj);
        private LW_GetEmuDistDelegate LW_GetEmuDist;

        private delegate point3d LW_GetEmuPositionDelegate(IntPtr obj);
        private LW_GetEmuPositionDelegate LW_GetEmuPosition;

        private delegate void LW_SetEmuHAngDelegate(IntPtr obj, float newEmuHAng);
        private LW_SetEmuHAngDelegate LW_SetEmuHAng;

        private delegate void LW_SetEmuVAngDelegate(IntPtr obj, float newEmuVAng);
        private LW_SetEmuVAngDelegate LW_SetEmuVAng;

        private delegate void LW_SetEmuDistDelegate(IntPtr obj, float newEmuDist);
        private LW_SetEmuDistDelegate LW_SetEmuDist;

        private delegate void LW_SetEmuPositionDelegate(IntPtr obj, float newEmuHAng, float newEmuVAng, float newEmuDist);
        private LW_SetEmuPositionDelegate LW_SetEmuPosition;

        private delegate double LW_GetTimeDelegate(IntPtr obj);
        private LW_GetTimeDelegate LW_GetTime;

        private delegate double LW_GetDeltaTimeDelegate(IntPtr obj);
        private LW_GetDeltaTimeDelegate LW_GetDeltaTime;

        private delegate double LW_GetVPSDelegate(IntPtr obj);
        private LW_GetVPSDelegate LW_GetVPS;

        private delegate int LW_KeyStateDelegate(IntPtr obj, int keyCode);
        private LW_KeyStateDelegate LW_KeyState;

        private delegate int LW_KeyOnDownDelegate(IntPtr obj, int keyCode);
        private LW_KeyOnDownDelegate LW_KeyOnDown;

        private delegate int LW_KeyIsDownDelegate(IntPtr obj, int keyCode);
        private LW_KeyIsDownDelegate LW_KeyIsDown;

        private delegate int LW_KeyOnUpDelegate(IntPtr obj, int keyCode);
        private LW_KeyOnUpDelegate LW_KeyOnUp;

        private delegate int LW_GetKeyReadDelegate(IntPtr obj);
        private LW_GetKeyReadDelegate LW_GetKeyRead;

        private delegate void LW_SetMouRawInputModeDelegate(IntPtr obj, bool newMouRawInputMode);
        private LW_SetMouRawInputModeDelegate LW_SetMouRawInputMode;

        private delegate bool LW_IsMouRawInputModeDelegate(IntPtr obj);
        private LW_IsMouRawInputModeDelegate LW_IsMouRawInputMode;

        private delegate int LW_GetMouPosXDelegate(IntPtr obj);
        private LW_GetMouPosXDelegate LW_GetMouPosX;

        private delegate int LW_GetMouPosYDelegate(IntPtr obj);
        private LW_GetMouPosYDelegate LW_GetMouPosY;

        private delegate int LW_GetMouButtonStateDelegate(IntPtr obj);
        private LW_GetMouButtonStateDelegate LW_GetMouButtonState;

        private delegate int LW_GetMouPrevButtonStateDelegate(IntPtr obj);
        private LW_GetMouPrevButtonStateDelegate LW_GetMouPrevButtonState;

        private delegate int LW_GetMouDeltaYDelegate(IntPtr obj);
        private LW_GetMouDeltaYDelegate LW_GetMouDeltaY;

        private delegate int LW_GetMouDeltaXDelegate(IntPtr obj);
        private LW_GetMouDeltaXDelegate LW_GetMouDeltaX;

        private delegate int LW_GetMouDeltaZDelegate(IntPtr obj);
        private LW_GetMouDeltaZDelegate LW_GetMouDeltaZ;

        private delegate int LW_GetMouButtonOnDownDelegate(IntPtr obj, int mouseButtonCode);
        private LW_GetMouButtonOnDownDelegate LW_GetMouButtonOnDown;

        private delegate int LW_GetMouButtonIsDownDelegate(IntPtr obj, int mouseButtonCode);
        private LW_GetMouButtonIsDownDelegate LW_GetMouButtonIsDown;

        private delegate int LW_GetMouButtonOnUpDelegate(IntPtr obj, int mouseButtonCode);
        private LW_GetMouButtonOnUpDelegate LW_GetMouButtonOnUp;

        private delegate int LW_GetMouLocationDelegate(IntPtr obj);
        private LW_GetMouLocationDelegate LW_GetMouLocation;

        private delegate vxl_mou_t LW_GetMouStructDelegate(IntPtr obj);
        private LW_GetMouStructDelegate LW_GetMouStruct;

        private delegate int LW_SetMouStructDelegate(IntPtr obj, vxl_mou_t newMouStruct);
        private LW_SetMouStructDelegate LW_SetMouStruct;

        private delegate int LW_SendMouseInputToNavDelegate(IntPtr obj, int navID);
        private LW_SendMouseInputToNavDelegate LW_SendMouseInputToNav;

        private delegate int LW_SetNavCountDelegate(IntPtr obj, int navCount);
        private LW_SetNavCountDelegate LW_SetNavCount;

        private delegate void LW_SetNavDeadZoneDelegate(IntPtr obj, float newNavDeadzone);
        private LW_SetNavDeadZoneDelegate LW_SetNavDeadZone;

        private delegate float LW_GetNavDeadZoneDelegate(IntPtr obj);
        private LW_GetNavDeadZoneDelegate LW_GetNavDeadZone;

        private delegate int LW_GetNavCountDelegate(IntPtr obj);
        private LW_GetNavCountDelegate LW_GetNavCount;

        private delegate vxl_nav_t LW_GetNavInputStructDelegate(IntPtr obj, int navID);
        private LW_GetNavInputStructDelegate LW_GetNavInputStruct;

        private delegate int LW_ReplaceNavInputStructDelegate(IntPtr obj, int navID, vxl_nav_t navData);
        private LW_ReplaceNavInputStructDelegate LW_ReplaceNavInputStruct;

        private delegate vxl_nav_t LW_GetNavRawInputDelegate(IntPtr obj, int navID);
        private LW_GetNavRawInputDelegate LW_GetNavRawInput;

        private delegate vxl_nav_t LW_GetNavRawPrevInputDelegate(IntPtr obj, int navID);
        private LW_GetNavRawPrevInputDelegate LW_GetNavRawPrevInput;

        private delegate float LW_GetNavAxisDelegate(IntPtr obj, int navID, int axisCode);
        private LW_GetNavAxisDelegate LW_GetNavAxis;

        private delegate int LW_GetNavButtonStateDelegate(IntPtr obj, int navID);
        private LW_GetNavButtonStateDelegate LW_GetNavButtonState;

        private delegate int LW_GetNavPrevButtonStateDelegate(IntPtr obj, int navID);
        private LW_GetNavPrevButtonStateDelegate LW_GetNavPrevButtonState;

        private delegate int LW_GetNavButtonIsDownDelegate(IntPtr obj, int navID, int navButtonCode);
        private LW_GetNavButtonIsDownDelegate LW_GetNavButtonIsDown;

        private delegate int LW_GetNavButtonOnDownDelegate(IntPtr obj, int navID, int navButtonCode);
        private LW_GetNavButtonOnDownDelegate LW_GetNavButtonOnDown;

        private delegate int LW_GetNavButtonOnUpDelegate(IntPtr obj, int navID, int navButtonCode);
        private LW_GetNavButtonOnUpDelegate LW_GetNavButtonOnUp;

        private delegate point3d LW_GetNavAngleDeltaDelegate(IntPtr obj, int navID);
        private LW_GetNavAngleDeltaDelegate LW_GetNavAngleDelta;

        private delegate point3d LW_GetNavDirectionDeltaDelegate(IntPtr obj, int navID);
        private LW_GetNavDirectionDeltaDelegate LW_GetNavDirectionDelta;

        private delegate point3d LW_GetNavSummedDeltaDelegate(IntPtr obj, int navID);
        private LW_GetNavSummedDeltaDelegate LW_GetNavSummedDelta;

        private delegate float LW_GetNavOrientationDelegate(IntPtr obj, int navID);
        private LW_GetNavOrientationDelegate LW_GetNavOrientation;

        private delegate int LW_SetNavOrientationDelegate(IntPtr obj, int navID, float orientationDegrees);
        private LW_SetNavOrientationDelegate LW_SetNavOrientation;

        private delegate int LW_GetNavCoordinateSystemDelegate(IntPtr obj);
        private LW_GetNavCoordinateSystemDelegate LW_GetNavCoordinateSystem;

        private delegate void LW_SetNavCoordinateSystemDelegate(IntPtr obj, int newValue);
        private LW_SetNavCoordinateSystemDelegate LW_SetNavCoordinateSystem;

        private delegate int LW_SetJoyAPITypeDelegate(IntPtr obj, int ApiType);
        private LW_SetJoyAPITypeDelegate LW_SetJoyAPIType;

        private delegate int LW_GetJoyAPITypeDelegate(IntPtr obj);
        private LW_GetJoyAPITypeDelegate LW_GetJoyAPIType;

        private delegate int LW_SetJoyDeadZoneDelegate(IntPtr obj, float newJoyDeadzone);
        private LW_SetJoyDeadZoneDelegate LW_SetJoyDeadZone;

        private delegate float LW_GetJoyDeadZoneDelegate(IntPtr obj);
        private LW_GetJoyDeadZoneDelegate LW_GetJoyDeadZone;

        private delegate int LW_GetJoyCountDelegate(IntPtr obj);
        private LW_GetJoyCountDelegate LW_GetJoyCount;

        private delegate int LW_ReplaceJoyInputStructDelegate(IntPtr obj, int joyID, vxl_joy_t joyData);
        private LW_ReplaceJoyInputStructDelegate LW_ReplaceJoyInputStruct;

        private delegate vxl_joy_t LW_GetJoyRawInputDelegate(IntPtr obj, int joyID);
        private LW_GetJoyRawInputDelegate LW_GetJoyRawInput;

        private delegate vxl_joy_t LW_GetJoyRawPrevInputDelegate(IntPtr obj, int joyID);
        private LW_GetJoyRawPrevInputDelegate LW_GetJoyRawPrevInput;

        private delegate float LW_GetJoyAxisValueDelegate(IntPtr obj, int joyID, int axis);
        private LW_GetJoyAxisValueDelegate LW_GetJoyAxisValue;

        private delegate point2d LW_GetJoyAxisValueP2DDelegate(IntPtr obj, int joyID, int stick);
        private LW_GetJoyAxisValueP2DDelegate LW_GetJoyAxisValueP2D;

        private delegate int LW_GetJoyButtonStateDelegate(IntPtr obj, int joyID);
        private LW_GetJoyButtonStateDelegate LW_GetJoyButtonState;

        private delegate int LW_GetJoyPrevButtonStateDelegate(IntPtr obj, int joyID);
        private LW_GetJoyPrevButtonStateDelegate LW_GetJoyPrevButtonState;

        private delegate int LW_GetJoyButtonIsDownDelegate(IntPtr obj, int joyID, int joyButtonCode);
        private LW_GetJoyButtonIsDownDelegate LW_GetJoyButtonIsDown;

        private delegate int LW_GetJoyButtonOnDownDelegate(IntPtr obj, int joyID, int joyButtonCode);
        private LW_GetJoyButtonOnDownDelegate LW_GetJoyButtonOnDown;

        private delegate int LW_GetJoyButtonOnUpDelegate(IntPtr obj, int joyID, int joyButtonCode);
        private LW_GetJoyButtonOnUpDelegate LW_GetJoyButtonOnUp;

        private delegate float LW_GetJoyTriggerValueDelegate(IntPtr obj, int joyID, int joyTriggerCode);
        private LW_GetJoyTriggerValueDelegate LW_GetJoyTriggerValue;

        private delegate float LW_GetJoyOrientationDelegate(IntPtr obj, int joyID);
        private LW_GetJoyOrientationDelegate LW_GetJoyOrientation;

        private delegate int LW_SetJoyOrientationDelegate(IntPtr obj, int joyID, float orientationDegrees);
        private LW_SetJoyOrientationDelegate LW_SetJoyOrientation;

        private delegate int LW_SetJoyVibrationDelegate(IntPtr obj, int joyID, float leftMotorSpeed, float rightMotorSpeed);
        private LW_SetJoyVibrationDelegate LW_SetJoyVibration;

        private delegate int LW_GetJoyAxisInversionDelegate(IntPtr obj, int joyID, int axisCode);
        private LW_GetJoyAxisInversionDelegate LW_GetJoyAxisInversion;

        private delegate int LW_SetJoyAxisInversionDelegate(IntPtr obj, int joyID, int axisCode, int value);
        private LW_SetJoyAxisInversionDelegate LW_SetJoyAxisInversion;

        private delegate int LW_EXT_KPLIB_KPZLOADDelegate(string filePath, ref tiletype tile);
        private LW_EXT_KPLIB_KPZLOADDelegate LW_EXT_KPLIB_KPZLOAD;

        private delegate void LW_RotVecDelegate(float rotationAmount, ref point3d a, ref point3d b, int useDegrees);
        private LW_RotVecDelegate LW_RotVec;

        private delegate bool LW_VxlMath_TransformCoordinatesDelegate(int FROM, int TO, ref point3d values);
        private LW_VxlMath_TransformCoordinatesDelegate LW_VxlMath_TransformCoordinates;

        private delegate float LW_VxlMath_TransformGetAxisDelegate(int WorldDirection, int CoordinateSystem, ref point3d value);
        private LW_VxlMath_TransformGetAxisDelegate LW_VxlMath_TransformGetAxis;

        #endregion

        #region MapingDelegates
        private void LedWinSetupDelegates(IntPtr ledWinHandle)
        {
            if (AreDelegatesMapped) return;

            // Define the function names and their corresponding delegate types
            var functionMap = new List<VoxonFunctionMapping>
            {
                new VoxonFunctionMapping("CreateLedWin", typeof(CreateLedWinDelegate), d => CreateLedWin = (CreateLedWinDelegate)d),
                new VoxonFunctionMapping("DeleteLedWin", typeof(DeleteLedWinDelegate), d => DeleteLedWin = (DeleteLedWinDelegate)d),
                new VoxonFunctionMapping("LW_QuitLoop", typeof(LW_QuitLoopDelegate), d => LW_QuitLoop = (LW_QuitLoopDelegate)d),
                new VoxonFunctionMapping("LW_InitApp", typeof(LW_InitAppDelegate), d => LW_InitApp = (LW_InitAppDelegate)d),
                new VoxonFunctionMapping("LW_UninitApp", typeof(LW_UninitAppDelegate), d => LW_UninitApp = (LW_UninitAppDelegate)d),
                new VoxonFunctionMapping("LW_GetVersion", typeof(LW_GetVersionDelegate), d => LW_GetVersion = (LW_GetVersionDelegate)d),
                new VoxonFunctionMapping("LW_GetBuildVersion", typeof(LW_GetBuildVersionDelegate), d => LW_GetBuildVersion = (LW_GetBuildVersionDelegate)d),
                new VoxonFunctionMapping("LW_SetVXLVersion", typeof(LW_SetVXLVersionDelegate), d => LW_SetVXLVersion = (LW_SetVXLVersionDelegate)d),
                new VoxonFunctionMapping("LW_GetTT", typeof(LW_GetTTDelegate), d => LW_GetTT = (LW_GetTTDelegate)d),
                new VoxonFunctionMapping("LW_Breath", typeof(LW_BreathDelegate), d => LW_Breath = (LW_BreathDelegate)d),
                new VoxonFunctionMapping("LW_SetFlags", typeof(LW_SetFlagsDelegate), d => LW_SetFlags = (LW_SetFlagsDelegate)d),
                new VoxonFunctionMapping("LW_SetFlag", typeof(LW_SetFlagDelegate), d => LW_SetFlag = (LW_SetFlagDelegate)d),
                new VoxonFunctionMapping("LW_IsFlagSet", typeof(LW_IsFlagSetDelegate), d => LW_IsFlagSet = (LW_IsFlagSetDelegate)d),
                new VoxonFunctionMapping("LW_GetFlags", typeof(LW_GetFlagsDelegate), d => LW_GetFlags = (LW_GetFlagsDelegate)d),
                new VoxonFunctionMapping("LW_SetWindRes", typeof(LW_SetWindResDelegate), d => LW_SetWindRes = (LW_SetWindResDelegate)d),
                new VoxonFunctionMapping("LW_MoveWind", typeof(LW_MoveWindDelegate), d => LW_MoveWind = (LW_MoveWindDelegate)d),
                new VoxonFunctionMapping("LW_SetProgName", typeof(LW_SetProgNameDelegate), d => LW_SetProgName = (LW_SetProgNameDelegate)d),
                new VoxonFunctionMapping("LW_IsWindowFocused", typeof(LW_IsWindowFocusedDelegate), d => LW_IsWindowFocused = (LW_IsWindowFocusedDelegate)d),
                new VoxonFunctionMapping("LW_GetWindowSizeInfo", typeof(LW_GetWindowSizeInfoDelegate), d => LW_GetWindowSizeInfo = (LW_GetWindowSizeInfoDelegate)d),
                new VoxonFunctionMapping("LW_StartDirectDraw", typeof(LW_StartDirectDrawDelegate), d => LW_StartDirectDraw = (LW_StartDirectDrawDelegate)d),
                new VoxonFunctionMapping("LW_StopDirectDraw", typeof(LW_StopDirectDrawDelegate), d => LW_StopDirectDraw = (LW_StopDirectDrawDelegate)d),
                new VoxonFunctionMapping("LW_NextPage", typeof(LW_NextPageDelegate), d => LW_NextPage = (LW_NextPageDelegate)d),
                new VoxonFunctionMapping("LW_DrawTxt", typeof(LW_DrawTxtDelegate), d => LW_DrawTxt = (LW_DrawTxtDelegate)d),
                new VoxonFunctionMapping("LW_DrawPixel", typeof(LW_DrawPixelDelegate), d => LW_DrawPixel = (LW_DrawPixelDelegate)d),
                new VoxonFunctionMapping("LW_DrawHLine", typeof(LW_DrawHLineDelegate), d => LW_DrawHLine = (LW_DrawHLineDelegate)d),
                new VoxonFunctionMapping("LW_DrawLine", typeof(LW_DrawLineDelegate), d => LW_DrawLine = (LW_DrawLineDelegate)d),
                new VoxonFunctionMapping("LW_DrawCircle", typeof(LW_DrawCircleDelegate), d => LW_DrawCircle = (LW_DrawCircleDelegate)d),
                new VoxonFunctionMapping("LW_DrawRect", typeof(LW_DrawRectDelegate), d => LW_DrawRect = (LW_DrawRectDelegate)d),
                new VoxonFunctionMapping("LW_DrawRectFill", typeof(LW_DrawRectFillDelegate), d => LW_DrawRectFill = (LW_DrawRectFillDelegate)d),
                new VoxonFunctionMapping("LW_DrawCircleFill", typeof(LW_DrawCircleFillDelegate), d => LW_DrawCircleFill = (LW_DrawCircleFillDelegate)d),
                new VoxonFunctionMapping("LW_DrawTile", typeof(LW_DrawTileDelegate), d => LW_DrawTile = (LW_DrawTileDelegate)d),
                new VoxonFunctionMapping("LW_Report", typeof(LW_ReportDelegate), d => LW_Report = (LW_ReportDelegate)d),
                new VoxonFunctionMapping("LW_GetEmuHAng", typeof(LW_GetEmuHAngDelegate), d => LW_GetEmuHAng = (LW_GetEmuHAngDelegate)d),
                new VoxonFunctionMapping("LW_GetEmuVAng", typeof(LW_GetEmuVAngDelegate), d => LW_GetEmuVAng = (LW_GetEmuVAngDelegate)d),
                new VoxonFunctionMapping("LW_GetEmuDist", typeof(LW_GetEmuDistDelegate), d => LW_GetEmuDist = (LW_GetEmuDistDelegate)d),
                new VoxonFunctionMapping("LW_GetEmuPosition", typeof(LW_GetEmuPositionDelegate), d => LW_GetEmuPosition = (LW_GetEmuPositionDelegate)d),
                new VoxonFunctionMapping("LW_SetEmuHAng", typeof(LW_SetEmuHAngDelegate), d => LW_SetEmuHAng = (LW_SetEmuHAngDelegate)d),
                new VoxonFunctionMapping("LW_SetEmuVAng", typeof(LW_SetEmuVAngDelegate), d => LW_SetEmuVAng = (LW_SetEmuVAngDelegate)d),
                new VoxonFunctionMapping("LW_SetEmuDist", typeof(LW_SetEmuDistDelegate), d => LW_SetEmuDist = (LW_SetEmuDistDelegate)d),
                new VoxonFunctionMapping("LW_SetEmuPosition", typeof(LW_SetEmuPositionDelegate), d => LW_SetEmuPosition = (LW_SetEmuPositionDelegate)d),
                new VoxonFunctionMapping("LW_GetTime", typeof(LW_GetTimeDelegate), d => LW_GetTime = (LW_GetTimeDelegate)d),
                new VoxonFunctionMapping("LW_GetDeltaTime", typeof(LW_GetDeltaTimeDelegate), d => LW_GetDeltaTime = (LW_GetDeltaTimeDelegate)d),
                new VoxonFunctionMapping("LW_GetVPS", typeof(LW_GetVPSDelegate), d => LW_GetVPS = (LW_GetVPSDelegate)d),
                new VoxonFunctionMapping("LW_KeyState", typeof(LW_KeyStateDelegate), d => LW_KeyState = (LW_KeyStateDelegate)d),
                new VoxonFunctionMapping("LW_KeyOnDown", typeof(LW_KeyOnDownDelegate), d => LW_KeyOnDown = (LW_KeyOnDownDelegate)d),
                new VoxonFunctionMapping("LW_KeyIsDown", typeof(LW_KeyIsDownDelegate), d => LW_KeyIsDown = (LW_KeyIsDownDelegate)d),
                new VoxonFunctionMapping("LW_KeyOnUp", typeof(LW_KeyOnUpDelegate), d => LW_KeyOnUp = (LW_KeyOnUpDelegate)d),
                new VoxonFunctionMapping("LW_GetKeyRead", typeof(LW_GetKeyReadDelegate), d => LW_GetKeyRead = (LW_GetKeyReadDelegate)d),
                new VoxonFunctionMapping("LW_SetMouRawInputMode", typeof(LW_SetMouRawInputModeDelegate), d => LW_SetMouRawInputMode = (LW_SetMouRawInputModeDelegate)d),
                new VoxonFunctionMapping("LW_IsMouRawInputMode", typeof(LW_IsMouRawInputModeDelegate), d => LW_IsMouRawInputMode = (LW_IsMouRawInputModeDelegate)d),
                new VoxonFunctionMapping("LW_GetMouPosX", typeof(LW_GetMouPosXDelegate), d => LW_GetMouPosX = (LW_GetMouPosXDelegate)d),
                new VoxonFunctionMapping("LW_GetMouPosY", typeof(LW_GetMouPosYDelegate), d => LW_GetMouPosY = (LW_GetMouPosYDelegate)d),
                new VoxonFunctionMapping("LW_GetMouButtonState", typeof(LW_GetMouButtonStateDelegate), d => LW_GetMouButtonState = (LW_GetMouButtonStateDelegate)d),
                new VoxonFunctionMapping("LW_GetMouPrevButtonState", typeof(LW_GetMouPrevButtonStateDelegate), d => LW_GetMouPrevButtonState = (LW_GetMouPrevButtonStateDelegate)d),
                new VoxonFunctionMapping("LW_GetMouDeltaY", typeof(LW_GetMouDeltaYDelegate), d => LW_GetMouDeltaY = (LW_GetMouDeltaYDelegate)d),
                new VoxonFunctionMapping("LW_GetMouDeltaX", typeof(LW_GetMouDeltaXDelegate), d => LW_GetMouDeltaX = (LW_GetMouDeltaXDelegate)d),
                new VoxonFunctionMapping("LW_GetMouDeltaZ", typeof(LW_GetMouDeltaZDelegate), d => LW_GetMouDeltaZ = (LW_GetMouDeltaZDelegate)d),
                new VoxonFunctionMapping("LW_GetMouButtonOnDown", typeof(LW_GetMouButtonOnDownDelegate), d => LW_GetMouButtonOnDown = (LW_GetMouButtonOnDownDelegate)d),
                new VoxonFunctionMapping("LW_GetMouButtonIsDown", typeof(LW_GetMouButtonIsDownDelegate), d => LW_GetMouButtonIsDown = (LW_GetMouButtonIsDownDelegate)d),
                new VoxonFunctionMapping("LW_GetMouButtonOnUp", typeof(LW_GetMouButtonOnUpDelegate), d => LW_GetMouButtonOnUp = (LW_GetMouButtonOnUpDelegate)d),
                new VoxonFunctionMapping("LW_GetMouLocation", typeof(LW_GetMouLocationDelegate), d => LW_GetMouLocation = (LW_GetMouLocationDelegate)d),
                new VoxonFunctionMapping("LW_GetMouStruct", typeof(LW_GetMouStructDelegate), d => LW_GetMouStruct = (LW_GetMouStructDelegate)d),
                new VoxonFunctionMapping("LW_SetMouStruct", typeof(LW_SetMouStructDelegate), d => LW_SetMouStruct = (LW_SetMouStructDelegate)d),
                new VoxonFunctionMapping("LW_SendMouseInputToNav", typeof(LW_SendMouseInputToNavDelegate), d => LW_SendMouseInputToNav = (LW_SendMouseInputToNavDelegate)d),
                new VoxonFunctionMapping("LW_SetNavCount", typeof(LW_SetNavCountDelegate), d => LW_SetNavCount = (LW_SetNavCountDelegate)d),
                new VoxonFunctionMapping("LW_SetNavDeadZone", typeof(LW_SetNavDeadZoneDelegate), d => LW_SetNavDeadZone = (LW_SetNavDeadZoneDelegate)d),
                new VoxonFunctionMapping("LW_GetNavDeadZone", typeof(LW_GetNavDeadZoneDelegate), d => LW_GetNavDeadZone = (LW_GetNavDeadZoneDelegate)d),
                new VoxonFunctionMapping("LW_GetNavCount", typeof(LW_GetNavCountDelegate), d => LW_GetNavCount = (LW_GetNavCountDelegate)d),
                new VoxonFunctionMapping("LW_GetNavInputStruct", typeof(LW_GetNavInputStructDelegate), d => LW_GetNavInputStruct = (LW_GetNavInputStructDelegate)d),
                new VoxonFunctionMapping("LW_ReplaceNavInputStruct", typeof(LW_ReplaceNavInputStructDelegate), d => LW_ReplaceNavInputStruct = (LW_ReplaceNavInputStructDelegate)d),
                new VoxonFunctionMapping("LW_GetNavRawInput", typeof(LW_GetNavRawInputDelegate), d => LW_GetNavRawInput = (LW_GetNavRawInputDelegate)d),
                new VoxonFunctionMapping("LW_GetNavRawPrevInput", typeof(LW_GetNavRawPrevInputDelegate), d => LW_GetNavRawPrevInput = (LW_GetNavRawPrevInputDelegate)d),
                new VoxonFunctionMapping("LW_GetNavAxis", typeof(LW_GetNavAxisDelegate), d => LW_GetNavAxis = (LW_GetNavAxisDelegate)d),
                new VoxonFunctionMapping("LW_GetNavButtonState", typeof(LW_GetNavButtonStateDelegate), d => LW_GetNavButtonState = (LW_GetNavButtonStateDelegate)d),
                new VoxonFunctionMapping("LW_GetNavPrevButtonState", typeof(LW_GetNavPrevButtonStateDelegate), d => LW_GetNavPrevButtonState = (LW_GetNavPrevButtonStateDelegate)d),
                new VoxonFunctionMapping("LW_GetNavButtonIsDown", typeof(LW_GetNavButtonIsDownDelegate), d => LW_GetNavButtonIsDown = (LW_GetNavButtonIsDownDelegate)d),
                new VoxonFunctionMapping("LW_GetNavButtonOnDown", typeof(LW_GetNavButtonOnDownDelegate), d => LW_GetNavButtonOnDown = (LW_GetNavButtonOnDownDelegate)d),
                new VoxonFunctionMapping("LW_GetNavButtonOnUp", typeof(LW_GetNavButtonOnUpDelegate), d => LW_GetNavButtonOnUp = (LW_GetNavButtonOnUpDelegate)d),
                new VoxonFunctionMapping("LW_GetNavAngleDelta", typeof(LW_GetNavAngleDeltaDelegate), d => LW_GetNavAngleDelta = (LW_GetNavAngleDeltaDelegate)d),
                new VoxonFunctionMapping("LW_GetNavDirectionDelta", typeof(LW_GetNavDirectionDeltaDelegate), d => LW_GetNavDirectionDelta = (LW_GetNavDirectionDeltaDelegate)d),
                new VoxonFunctionMapping("LW_GetNavSummedDelta", typeof(LW_GetNavSummedDeltaDelegate), d => LW_GetNavSummedDelta = (LW_GetNavSummedDeltaDelegate)d),
                new VoxonFunctionMapping("LW_GetNavOrientation", typeof(LW_GetNavOrientationDelegate), d => LW_GetNavOrientation = (LW_GetNavOrientationDelegate)d),
                new VoxonFunctionMapping("LW_SetNavOrientation", typeof(LW_SetNavOrientationDelegate), d => LW_SetNavOrientation = (LW_SetNavOrientationDelegate)d),
                new VoxonFunctionMapping("LW_GetNavCoordinateSystem", typeof(LW_GetNavCoordinateSystemDelegate), d => LW_GetNavCoordinateSystem = (LW_GetNavCoordinateSystemDelegate)d),
                new VoxonFunctionMapping("LW_SetNavCoordinateSystem", typeof(LW_SetNavCoordinateSystemDelegate), d => LW_SetNavCoordinateSystem = (LW_SetNavCoordinateSystemDelegate)d),
                new VoxonFunctionMapping("LW_SetJoyAPIType", typeof(LW_SetJoyAPITypeDelegate), d => LW_SetJoyAPIType = (LW_SetJoyAPITypeDelegate)d),
                new VoxonFunctionMapping("LW_GetJoyAPIType", typeof(LW_GetJoyAPITypeDelegate), d => LW_GetJoyAPIType = (LW_GetJoyAPITypeDelegate)d),
                new VoxonFunctionMapping("LW_SetJoyDeadZone", typeof(LW_SetJoyDeadZoneDelegate), d => LW_SetJoyDeadZone = (LW_SetJoyDeadZoneDelegate)d),
                new VoxonFunctionMapping("LW_GetJoyDeadZone", typeof(LW_GetJoyDeadZoneDelegate), d => LW_GetJoyDeadZone = (LW_GetJoyDeadZoneDelegate)d),
                new VoxonFunctionMapping("LW_GetJoyCount", typeof(LW_GetJoyCountDelegate), d => LW_GetJoyCount = (LW_GetJoyCountDelegate)d),
                new VoxonFunctionMapping("LW_ReplaceJoyInputStruct", typeof(LW_ReplaceJoyInputStructDelegate), d => LW_ReplaceJoyInputStruct = (LW_ReplaceJoyInputStructDelegate)d),
                new VoxonFunctionMapping("LW_GetJoyRawInput", typeof(LW_GetJoyRawInputDelegate), d => LW_GetJoyRawInput = (LW_GetJoyRawInputDelegate)d),
                new VoxonFunctionMapping("LW_GetJoyRawPrevInput", typeof(LW_GetJoyRawPrevInputDelegate), d => LW_GetJoyRawPrevInput = (LW_GetJoyRawPrevInputDelegate)d),
                new VoxonFunctionMapping("LW_GetJoyAxisValue", typeof(LW_GetJoyAxisValueDelegate), d => LW_GetJoyAxisValue = (LW_GetJoyAxisValueDelegate)d),
                new VoxonFunctionMapping("LW_GetJoyAxisValueP2D", typeof(LW_GetJoyAxisValueP2DDelegate), d => LW_GetJoyAxisValueP2D = (LW_GetJoyAxisValueP2DDelegate)d),
                new VoxonFunctionMapping("LW_GetJoyButtonState", typeof(LW_GetJoyButtonStateDelegate), d => LW_GetJoyButtonState = (LW_GetJoyButtonStateDelegate)d),
                new VoxonFunctionMapping("LW_GetJoyPrevButtonState", typeof(LW_GetJoyPrevButtonStateDelegate), d => LW_GetJoyPrevButtonState = (LW_GetJoyPrevButtonStateDelegate)d),
                new VoxonFunctionMapping("LW_GetJoyButtonIsDown", typeof(LW_GetJoyButtonIsDownDelegate), d => LW_GetJoyButtonIsDown = (LW_GetJoyButtonIsDownDelegate)d),
                new VoxonFunctionMapping("LW_GetJoyButtonOnDown", typeof(LW_GetJoyButtonOnDownDelegate), d => LW_GetJoyButtonOnDown = (LW_GetJoyButtonOnDownDelegate)d),
                new VoxonFunctionMapping("LW_GetJoyButtonOnUp", typeof(LW_GetJoyButtonOnUpDelegate), d => LW_GetJoyButtonOnUp = (LW_GetJoyButtonOnUpDelegate)d),
                new VoxonFunctionMapping("LW_GetJoyTriggerValue", typeof(LW_GetJoyTriggerValueDelegate), d => LW_GetJoyTriggerValue = (LW_GetJoyTriggerValueDelegate)d),
                new VoxonFunctionMapping("LW_GetJoyOrientation", typeof(LW_GetJoyOrientationDelegate), d => LW_GetJoyOrientation = (LW_GetJoyOrientationDelegate)d),
                new VoxonFunctionMapping("LW_SetJoyOrientation", typeof(LW_SetJoyOrientationDelegate), d => LW_SetJoyOrientation = (LW_SetJoyOrientationDelegate)d),
                new VoxonFunctionMapping("LW_SetJoyVibration", typeof(LW_SetJoyVibrationDelegate), d => LW_SetJoyVibration = (LW_SetJoyVibrationDelegate)d),
                new VoxonFunctionMapping("LW_GetJoyAxisInversion", typeof(LW_GetJoyAxisInversionDelegate), d => LW_GetJoyAxisInversion = (LW_GetJoyAxisInversionDelegate)d),
                new VoxonFunctionMapping("LW_SetJoyAxisInversion", typeof(LW_SetJoyAxisInversionDelegate), d => LW_SetJoyAxisInversion = (LW_SetJoyAxisInversionDelegate)d),
                new VoxonFunctionMapping("LW_EXT_KPLIB_KPZLOAD", typeof(LW_EXT_KPLIB_KPZLOADDelegate), d => LW_EXT_KPLIB_KPZLOAD = (LW_EXT_KPLIB_KPZLOADDelegate)d),
                new VoxonFunctionMapping("LW_RotVec", typeof(LW_RotVecDelegate), d => LW_RotVec = (LW_RotVecDelegate)d),
                new VoxonFunctionMapping("LW_VxlMath_TransformCoordinates", typeof(LW_VxlMath_TransformCoordinatesDelegate), d => LW_VxlMath_TransformCoordinates = (LW_VxlMath_TransformCoordinatesDelegate)d),
                new VoxonFunctionMapping("LW_VxlMath_TransformGetAxis", typeof(LW_VxlMath_TransformGetAxisDelegate), d => LW_VxlMath_TransformGetAxis = (LW_VxlMath_TransformGetAxisDelegate)d),

            };

            // Iterate through the function map and map the delegates
            foreach (var mapping in functionMap)
            {
                IntPtr funcaddr = NativeMethods.GetProcAddress(ledWinHandle, mapping.FunctionName);
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

        #endregion

        public Dictionary<string, bool> GetDelegateStatus()
        {
            return new Dictionary<string, bool>(delegateMappingStatus);
        }

        // 
        // LedWin_CS container functions
        // -----------------------------
        //

        public IntPtr LWCHandl; //LedWin Class Pointer

        // Start LedWin by passing the path to the LedWin DLL
        public LedWinCS(string LedWinDLLPath = null)
        {
            if (LedWinDLLPath == null)
            {
                LedWinDLLPath = "LedWin.dll";
            }

            ledWinDLLHandle = NativeMethods.LoadLibrary(LedWinDLLPath);
            if (ledWinDLLHandle == IntPtr.Zero)
            {
                throw new DllNotFoundException($"The specified DLL 'LedWin.dll' could not be found in {LedWinDLLPath}");

            }

            this.LedWinSetupDelegates(ledWinDLLHandle);

            ledWinDLLPath = Path.GetFullPath(LedWinDLLPath);
            LWCHandl = CreateLedWin();
        }

        // Start LedWin by passing the handle to the LedWin DLL
        public LedWinCS(IntPtr _LedWinHandle)
        {
            if (_LedWinHandle == IntPtr.Zero)
            {
                throw new ArgumentException("Invalid handle provided.", nameof(_LedWinHandle));
            }

            try
            {
                this.LedWinSetupDelegates(ledWinDLLHandle);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error setting up LedWin delegates: {ex.Message}");
            }
            LWCHandl = CreateLedWin();
            ledWinDLLPath = "LedWin.dll";
        }

        public IntPtr GetLedWinDLLHandle()
        {
            return ledWinDLLHandle;
        }

        ~LedWinCS()
        {
            Dispose(false);
        }

        // Implement IDisposable to clean up the native object when done
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {

            if (LWCHandl != IntPtr.Zero)
            {

                if (DeleteLedWin(LWCHandl) == 0)
                {
                    LWCHandl = IntPtr.Zero;
                }
                else
                {
                    new Exception($"Couldn't delete LedWin!");
                }
            }

        }


        #region High Level Functions

        // High Level functions are unique functions for LedWinCS which make developing in C# 
        // easier

        // LedWin CS Functions 

        /// <summary>
        /// Initializes LedWin, enables the DirectDraw (DD) Buffer flag, and positions the window as specified.
        /// </summary>
        /// <param name="programName">The name of the program to set in LedWin.</param>
        /// <param name="xRes">The horizontal resolution of the window.</param>
        /// <param name="yRes">The vertical resolution of the window.</param>
        /// <param name="xPos">The horizontal position of the window on the display.</param>
        /// <param name="yPos">The vertical position of the window on the display.</param>
        /// 
        /// <returns>True if initialization was successful; otherwise, false.</returns>
        public bool LedWinInit(string programName, int xRes, int yRes, int xPos, int yPos, int launchFlags = 0)
        {
            // Validate the LedWin handle
            if (LWCHandl == IntPtr.Zero)
            {
                Console.WriteLine("Error: LedWin handle is not initialized.");
                return false;
            }

            try
            {

                // Set the program name
                if (string.IsNullOrWhiteSpace(programName))
                {
                    programName = " ";
                }
                SetProgName(programName);

                // Enable the DirectDraw buffer flag
                SetFlag(1, (int)LW_FLAGS.LW_FLAG_USE_DD_BUFFER);

                // Initialize the window
                InitWindow(launchFlags);

                // Set resolution
                if (xRes <= 0 || yRes <= 0)
                {
                    Console.WriteLine("Error: Invalid resolution values.");
                    return false;
                }
                SetXRes(xRes);
                SetYRes(yRes);

                // Move the window to the specified position
                MoveWindow(xPos, yPos);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during LedWin initialization: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Simple method for rendering LedWin content.
        /// </summary>
        /// <param name="LedHostCS"></param>
        /// <param name="vs"></param>
        public void Render(ref LedHostCS LedHostCS, ref vxl_state_t vs)
        {
            if (this.StartDirectDraw(ref GetTT()) == 1)
            {

                UpdateWindowSizeInfo();
                if (IsFlagSet((int)LW_FLAGS.LW_FLAG_EXCLUSIVE_LED) == false ||
                    vs.rpm <= 0 && IsFlagSet((int)LW_FLAGS.LW_FLAG_EXCLUSIVE_LED) == true)
                {
                    LedHostCS.Rend2D(ref vs, ref GetTT(), GetEmuHAng(), GetEmuVAng(),
                        GetEmuDist());
                }

                StopDirectDraw();
                NextPage();
            }
        }


        public bool TestHandle()
        {
            if (LWCHandl == IntPtr.Zero)
            {
                return false;
            }
            return true;
        }

        public static bool Test() // Bare Bones LedWin Test
        {
            IntPtr intPtr = IntPtr.Zero; // Placeholder for the DLL handle
            vxl_state_t vs = new vxl_state_t();
            LedWinCS ledWinCS = new LedWinCS();

            // Initialize LedWin
            if (!ledWinCS.LedWinInit("LedWin Test", 600, 800, 200, 200))
            {
                Console.Error.WriteLine("Failed to initialize LedWin.");
                return false;
            }

            // Call a test method
            try
            {
                ledWinCS.GetVersion();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error calling test method (GetVersion): {ex.Message}");
                return false;
            }


            while (ledWinCS.Breath() == 0)
            {
                //-------------------------------------------------------------
                // Input 

                if (ledWinCS.GetKeyStatus(VX_KEYS.KB_Escape) == 1)
                {
                    ledWinCS.QuitLoop();
                }

                //-------------------------------------------------------------
                // LedWin / 2D Render Calls

                ledWinCS.DrawTxt(0, 0, 0x00ff00, -1, "LedWin Test Press 'ESC' to quit");
                ledWinCS.Report(LW_REPORTS.LW_REPORT_VPS, ledWinCS.currentWindowResX - 250, ledWinCS.currentWindowResY - 100, ref vs);

                if (ledWinCS.StartDirectDraw(ref ledWinCS.GetTT()) == 1)
                {
                    ledWinCS.UpdateWindowSizeInfo();
                    ledWinCS.StopDirectDraw();
                    ledWinCS.NextPage();
                }
            }

            ledWinCS.UninitWindow();
            ledWinCS.Dispose();
            return true;
        }

        public string GetLedWinDLLPath()
        {
            return ledWinDLLPath;
        }

        #endregion


        #region LedWin Function Wrappers
        //
        // LedWin Function wrappers 
        // =----------------------=
        //
        // These are the functions that the C# developer will use which are 
        // routed to the C++ DLL functions
        //


        // Engine Control 

        public void QuitLoop()
        {
            if (LWCHandl == IntPtr.Zero) return;
            LW_QuitLoop(LWCHandl);
        }

        public int InitWindow(int launchFlags = 0)
        {
            if (LWCHandl == IntPtr.Zero) return -1;
            int result = LW_InitApp(LWCHandl, launchFlags);
            if (result == 0) return result;

            UpdateWindowSizeInfo();
            return result;
        }

        public void UninitWindow()
        {
            if (LWCHandl == IntPtr.Zero) return;
            LW_UninitApp(LWCHandl);
        }

        public ulong GetVersion()
        {
            if (LWCHandl == IntPtr.Zero) return 0;
            return
            LW_GetVersion(LWCHandl);
        }

        public string GetBuildVersion()
        {
            if (LWCHandl == IntPtr.Zero) return " ";
            string version = Marshal.PtrToStringAnsi(LW_GetBuildVersion(LWCHandl));
            return version;

        }


        public void SetVXLVersion(long setVXLVersion)
        {
            if (LWCHandl == IntPtr.Zero) return;

            LW_SetVXLVersion(LWCHandl, setVXLVersion);
        }


        public ref tiletype GetTT()
        {
            // tiletype dummyTT = new tiletype();

            if (LWCHandl == IntPtr.Zero) throw new Exception("Error LWCHandl is null");
            return ref LW_GetTT(LWCHandl);
        }

        public int Breath()
        {
            if (LWCHandl == IntPtr.Zero) return -1;
            return LW_Breath(LWCHandl);
        }


        // Sets an individual flag without replacing the existing flags
        public void SetFlag(int value, int flag)
        {
            if (LWCHandl == IntPtr.Zero) return;
            LW_SetFlag(LWCHandl, value, flag);
        }

        // Overwrites the existing flags with the new settings flag
        public void SetFlags(int newFlagSetting)
        {
            if (LWCHandl == IntPtr.Zero) return;
            LW_SetFlags(LWCHandl, newFlagSetting);
        }

        public bool IsFlagSet(int flag)
        {
            if (LWCHandl == IntPtr.Zero) return false;
            return LW_IsFlagSet(LWCHandl, flag);
        }

        public int GetFlags()
        {
            if (LWCHandl == IntPtr.Zero) return 0;
            return LW_GetFlags(LWCHandl);
        }


        // Window Control
        public void UpdateWindowSizeInfo()
        {
            if (LWCHandl == IntPtr.Zero) return;
            LW_GetWindowSizeInfo(LWCHandl, ref currentWindowPosX, ref currentWindowPosY, ref currentWindowResX, ref currentWindowResY);
        }
        public void MoveWindow(int xpos, int ypos)
        {
            if (LWCHandl == IntPtr.Zero) return;
            LW_MoveWind(LWCHandl, xpos, ypos);
            UpdateWindowSizeInfo();
        }

        public void SetWindowRes(int xres, int yres)
        {
            if (LWCHandl == IntPtr.Zero) return;
            LW_SetWindRes(LWCHandl, xres, yres);
            UpdateWindowSizeInfo();
        }

        public void SetXRes(int xres)
        {
            if (LWCHandl == IntPtr.Zero) return;
            SetWindowRes(xres, currentWindowResY);
        }

        public void SetYRes(int yres)
        {
            if (LWCHandl == IntPtr.Zero) return;
            SetWindowRes(currentWindowResX, yres);
        }

        public void SetProgName(string progname)
        {
            if (LWCHandl == IntPtr.Zero) return;


            LW_SetProgName(LWCHandl, progname);  // string conversion happens on the C/C++ side
        }

        public int IsWindowFocused()
        {
            if (LWCHandl == IntPtr.Zero) return -1;


            return LW_IsWindowFocused(LWCHandl);  // string converstion happens on the C/C++ side strie ENC
        }

        vxl_state_t vsCpy;
        // Reports
        public int Report(LW_REPORTS reportenum, int xPos, int yPos, ref vxl_state_t vs)
        {

            if (currentWindowResX < 200 || currentWindowResY < 200) return -1;

            if (LWCHandl == IntPtr.Zero) return -1;
            if (reportenum == LW_REPORTS.LW_REPORT_VXL_STATE)
            {
                vsCpy = vs;
                LW_Report(LWCHandl, (int)reportenum, xPos, yPos, ref vs);
            }
            return LW_Report(LWCHandl, (int)reportenum, xPos, yPos, ref vsCpy);
        }


        // Direct 2D Draw

        public int StartDirectDraw(ref tiletype tileDD)
        {
            if (LWCHandl == IntPtr.Zero) return -1;
            return LW_StartDirectDraw(LWCHandl, ref tileDD);
        }

        public void StopDirectDraw()
        {
            if (LWCHandl == IntPtr.Zero) return;
            LW_StopDirectDraw(LWCHandl);
        }
        public void NextPage()
        {
            if (LWCHandl == IntPtr.Zero) return;
            LW_NextPage(LWCHandl);
        }



        // 2D Drawing Calls

        public void DrawTxt(int x, int y, int fcol, int bcol, string msg) /* const char fmt[1024] LedWin.DLL expecting*/
        {
            if (LWCHandl == IntPtr.Zero) return;
            LW_DrawTxt(LWCHandl, x, y, fcol, bcol, msg);
        }
        public void DrawPixel(int x, int y, int col)
        {
            if (LWCHandl == IntPtr.Zero) return;
            LW_DrawPixel(LWCHandl, x, y, col);
        }

        public void DrawHLine(int x0, int x1, int y, int col)
        {
            if (LWCHandl == IntPtr.Zero) return;
            LW_DrawHLine(LWCHandl, x0, x1, y, col);
        }

        public void DrawLine(float x0, float y0, float x1, float y1, int col)
        {
            if (LWCHandl == IntPtr.Zero) return;
            LW_DrawLine(LWCHandl, x0, y0, x1, y1, col);
        }

        public void DrawCircle(int xc, int yc, int r, int col)
        {
            if (LWCHandl == IntPtr.Zero) return;
            LW_DrawCircle(LWCHandl, xc, yc, r, col);
        }

        public void DrawRect(int x0, int y0, int x1, int y1, int col)
        {
            if (LWCHandl == IntPtr.Zero) return;
            LW_DrawRect(LWCHandl, x0, y0, x1, y1, col);
        }

        public void DrawRectFill(int x0, int y0, int x1, int y1, int col)
        {
            if (LWCHandl == IntPtr.Zero) return;
            LW_DrawRectFill(LWCHandl, x0, y0, x1, y1, col);
        }

        public void DrawCircleFill(int xc, int yc, int r, int col)
        {
            if (LWCHandl == IntPtr.Zero) return;
            LW_DrawCircleFill(LWCHandl, xc, yc, r, col);
        }

        public void DrawTile(ref tiletype image, int xOffset, int yOffset)
        {
            if (LWCHandl == IntPtr.Zero) return;
            LW_DrawTile(LWCHandl, ref image, xOffset, yOffset);
        }




        // Emulator Controls

        public float GetEmuHAng()
        {
            if (LWCHandl == IntPtr.Zero) return -1;
            return LW_GetEmuHAng(LWCHandl);
        }

        public float GetEmuVAng()
        {
            if (LWCHandl == IntPtr.Zero) return -1;
            return LW_GetEmuVAng(LWCHandl);
        }

        public float GetEmuDist()
        {
            if (LWCHandl == IntPtr.Zero) return -1;
            return LW_GetEmuDist(LWCHandl);
        }

        public point3d GetEmuPosition()
        {
            if (LWCHandl == IntPtr.Zero) return new point3d();
            return LW_GetEmuPosition(LWCHandl);
        }

        public void SetEmuHAng(float newEmuHAng)
        {
            if (LWCHandl == IntPtr.Zero) return;
            LW_SetEmuHAng(LWCHandl, newEmuHAng);
        }

        public void SetEmuVAng(float newEmuVAng)
        {
            if (LWCHandl == IntPtr.Zero) return;
            LW_SetEmuVAng(LWCHandl, newEmuVAng);
        }

        public void SetEmuDist(float newEmuDist)
        {
            if (LWCHandl == IntPtr.Zero) return;
            LW_SetEmuDist(LWCHandl, newEmuDist);
        }

        public void SetEmuPosition(point3d newEmuPostion)
        {
            SetEmuPosition(newEmuPostion.x, newEmuPostion.y, newEmuPostion.z);
        }

        public void SetEmuPosition(float newEmuHAng, float newEmuVAng, float newEmuDist)
        {
            if (LWCHandl == IntPtr.Zero) return;
            LW_SetEmuPosition(LWCHandl, newEmuHAng, newEmuVAng, newEmuDist);
        }

        // Timers

        public double GetTime()
        {
            if (LWCHandl == IntPtr.Zero) return -1;
            return LW_GetTime(LWCHandl);
        }

        public double GetDeltaTimeDouble()
        {
            if (LWCHandl == IntPtr.Zero) return -1;
            return LW_GetDeltaTime(LWCHandl);
        }
        public float GetDeltaTime()
        {
            if (LWCHandl == IntPtr.Zero) return -1;
            return (float)LW_GetDeltaTime(LWCHandl);
        }


        public double GetVPS()
        {
            if (LWCHandl == IntPtr.Zero) return -1;
            return LW_GetVPS(LWCHandl);
        }



        // Input



        public int GetKeyState(int keyCode)
        {
            if (LWCHandl == IntPtr.Zero) return -1;
            return LW_KeyState(LWCHandl, keyCode);
        }

        public int GetKeyState(VX_KEYS keyCode)
            => GetKeyState((int)keyCode);

        public int GetKeyStatus(int keyCode)
        {
            if (LWCHandl == IntPtr.Zero) return -1;
            return LW_KeyState(LWCHandl, keyCode);
        }

        public int GetKeyStatus(VX_KEYS keyCode)
            => GetKeyStatus((int)keyCode);


        public int GetKeyOnDown(int keyCode)
        {
            if (LWCHandl == IntPtr.Zero) return -1;
            return LW_KeyOnDown(LWCHandl, keyCode);
        }

        public int GetKeyOnDown(VX_KEYS keyCode)
            => GetKeyOnDown((int)keyCode);

        public int GetKeyIsDown(int keyCode)
        {
            if (LWCHandl == IntPtr.Zero) return -1;
            return LW_KeyIsDown(LWCHandl, keyCode);
        }

        public int GetKeyIsDown(VX_KEYS keyCode)
          => GetKeyIsDown((int)keyCode);

        public int GetKeyOnUp(int keyCode)
        {
            if (LWCHandl == IntPtr.Zero) return -1;
            return LW_KeyOnUp(LWCHandl, keyCode);
        }

        public int GetKeyOnUp(VX_KEYS keyCode)
          => GetKeyOnUp((int)keyCode);

        public int GetKeyRead()
        {
            if (LWCHandl == IntPtr.Zero) return 0;
            return LW_GetKeyRead(LWCHandl);
        }

        // Mouse
        public void SetMouRawInputMode(bool newMouRawInputMode)
        {
            if (LWCHandl == IntPtr.Zero) return;
            LW_SetMouRawInputMode(LWCHandl, newMouRawInputMode);
        }
        public bool IsMouRawInputMode()
        {
            if (LWCHandl == IntPtr.Zero) return false;
            return LW_IsMouRawInputMode(LWCHandl);
        }
        public int GetMouPosX()
        {
            if (LWCHandl == IntPtr.Zero) return 0;
            return LW_GetMouPosX(LWCHandl);
        }
        public int GetMouPosY()
        {
            if (LWCHandl == IntPtr.Zero) return 0;
            return LW_GetMouPosY(LWCHandl);
        }
        public int GetMouButtonState()
        {
            if (LWCHandl == IntPtr.Zero) return 0;
            return LW_GetMouButtonState(LWCHandl);
        }
        public int GetMouPrevButtonState()
        {
            if (LWCHandl == IntPtr.Zero) return 0;
            return LW_GetMouPrevButtonState(LWCHandl);
        }
        public int GetMouDeltaX()
        {
            if (LWCHandl == IntPtr.Zero) return 0;
            return LW_GetMouDeltaX(LWCHandl);
        }
        public int GetMouDeltaY()
        {
            if (LWCHandl == IntPtr.Zero) return 0;
            return LW_GetMouDeltaY(LWCHandl);
        }
        public int GetMouDeltaZ()
        {
            if (LWCHandl == IntPtr.Zero) return 0;
            return LW_GetMouDeltaZ(LWCHandl);
        }
        public int GetMouButtonOnDown(int mouseButtonCode)
        {
            if (LWCHandl == IntPtr.Zero) return 0;
            return LW_GetMouButtonOnDown(LWCHandl, mouseButtonCode);
        }

        public int GetMouButtonOnDown(VX_MOUSE_BUTTON_CODES mouseButtonCode)
            => GetMouButtonOnDown((int)mouseButtonCode);

        public int GetMouButtonIsDown(int mouseButtonCode)
        {
            if (LWCHandl == IntPtr.Zero) return 0;
            return LW_GetMouButtonIsDown(LWCHandl, mouseButtonCode);
        }

        public int GetMouButtonIsDown(VX_MOUSE_BUTTON_CODES mouseButtonCode)
            => GetMouButtonIsDown((int)mouseButtonCode);

        public int GetMouButtonOnUp(int mouseButtonCode)
        {
            if (LWCHandl == IntPtr.Zero) return 0;
            return LW_GetMouButtonOnUp(LWCHandl, mouseButtonCode);
        }

        public int GetMouButtonOnUp(VX_MOUSE_BUTTON_CODES mouseButtonCode)
            => GetMouButtonOnUp((int)mouseButtonCode);

        public int GetMouLocation()
        {
            if (LWCHandl == IntPtr.Zero) return 0;
            return LW_GetMouLocation(LWCHandl);
        }

        public vxl_mou_t GetMouStruct()
        {
            if (LWCHandl == IntPtr.Zero) return new vxl_mou_t();
            return LW_GetMouStruct(LWCHandl);
        }

        public int SetMouStruct(vxl_mou_t newMouStruct)
        {
            if (LWCHandl == IntPtr.Zero) return 1;
            return LW_SetMouStruct(LWCHandl, newMouStruct);
        }

        public void SendMouseInputToNav(int navID)
        {
            if (LWCHandl == IntPtr.Zero) return;
            LW_SendMouseInputToNav(LWCHandl, navID);
        }

        // SpaceNav

        public int SetNavCount(int navCount)
        {
            if (LWCHandl == IntPtr.Zero) return 0;
            return LW_SetNavCount(LWCHandl, navCount);

        }

        public void SetNavDeadZone(float newDeadZone)
        {
            if (LWCHandl == IntPtr.Zero) return;
            LW_SetNavDeadZone(LWCHandl, newDeadZone);
        }

        public float GetNavDeadZone()
        {
            if (LWCHandl == IntPtr.Zero) return 0;
            return LW_GetNavDeadZone(LWCHandl);
        }

        public int GetNavCount()
        {
            if (LWCHandl == IntPtr.Zero) return 0;
            return LW_GetNavCount(LWCHandl);
        }

        public vxl_nav_t GetNavInputStruct(int navID)
        {
            if (LWCHandl == IntPtr.Zero) return new vxl_nav_t();
            return LW_GetNavInputStruct(LWCHandl, navID);
        }

        public int ReplaceNavInputStruct(int navID, vxl_nav_t navData)
        {
            if (LWCHandl == IntPtr.Zero) return 1;

            return (LW_ReplaceNavInputStruct(LWCHandl, navID, navData));
        }

        public vxl_nav_t GetNavRawInputStruct(int navID, vxl_nav_t navData)
        {
            if (LWCHandl == IntPtr.Zero) return new vxl_nav_t();
            return (LW_GetNavRawInput(LWCHandl, navID));
        }

        public vxl_nav_t GetNavRawPrevInputStruct(int navID, vxl_nav_t navData)
        {
            if (LWCHandl == IntPtr.Zero) return new vxl_nav_t();
            return (LW_GetNavRawPrevInput(LWCHandl, navID));
        }

        public float GetNavAxisValue(int navID, int axisCode)
        {
            if (LWCHandl == IntPtr.Zero) return 0;
            return (LW_GetNavAxis(LWCHandl, navID, axisCode));
        }

        public float GetNavAxisValue(int navID, VX_NAV_AXIS_CODES axisCode)
            => GetNavAxisValue(navID, (int)axisCode);

        public int GetNavButtonState(int navID)
        {
            if (LWCHandl == IntPtr.Zero) return 0;
            return (LW_GetNavButtonState(LWCHandl, navID));
        }

        public int GetNavPrevButtonState(int navID)
        {
            if (LWCHandl == IntPtr.Zero) return 0;
            return (LW_GetNavPrevButtonState(LWCHandl, navID));
        }

        public int GetNavButtonIsDown(int navID, int navButtonCode)
        {
            if (LWCHandl == IntPtr.Zero) return 0;
            return (LW_GetNavButtonIsDown(LWCHandl, navID, (int)navButtonCode));
        }

        public int GetNavButtonIsDown(int navID, VX_NAV_BUTTON_CODES navButtonCode)
            => GetNavButtonIsDown(navID, (int)navButtonCode);


        public int GetNavButtonOnDown(int navID, int navButtonCode)
        {
            if (LWCHandl == IntPtr.Zero) return 0;
            return (LW_GetNavButtonOnDown(LWCHandl, navID, navButtonCode));
        }

        public int GetNavButtonOnDown(int navID, VX_NAV_BUTTON_CODES navButtonCode)
            => GetNavButtonOnDown(navID, (int)navButtonCode);

        public int GetNavButtonOnUp(int navID, int navButtonCode)
        {
            if (LWCHandl == IntPtr.Zero) return 0;
            return (LW_GetNavButtonOnUp(LWCHandl, navID, (int)navButtonCode));
        }

        public int GetNavButtonOnUp(int navID, VX_NAV_BUTTON_CODES navButtonCode)
            => GetNavButtonOnUp(navID, (int)navButtonCode);


        public point3d GetNavAngleDelta(int navID)
        {
            if (LWCHandl == IntPtr.Zero) return new point3d();
            return (LW_GetNavAngleDelta(LWCHandl, navID));
        }

        public point3d GetNavDirectionDelta(int navID)
        {
            if (LWCHandl == IntPtr.Zero) return new point3d();
            return (LW_GetNavDirectionDelta(LWCHandl, navID));
        }

        public point3d GetNavSummedDelta(int navID)
        {
            if (LWCHandl == IntPtr.Zero) return new point3d();
            return (LW_GetNavSummedDelta(LWCHandl, navID));
        }

        public float GetNavOrientation(int navID)
        {
            if (LWCHandl == IntPtr.Zero) return new float();
            return (LW_GetNavOrientation(LWCHandl, navID));
        }

        public int SetNavOrientation(int navID, float orientationNavDegrees)
        {
            if (LWCHandl == IntPtr.Zero) return new int();
            return (LW_SetNavOrientation(LWCHandl, navID, orientationNavDegrees));
        }

        public int GetNavCoordinateSystem()
        {
            if (LWCHandl == IntPtr.Zero) return 0;
            return (LW_GetNavCoordinateSystem(LWCHandl));
        }

        public void SetNavCoordinateSystem(int newValue)
        {
            if (LWCHandl == IntPtr.Zero) return;
            LW_SetNavCoordinateSystem(LWCHandl, newValue);
        }





        // Joystick Functions

        public int SetJoyAPIType(int ApiType)
        {
            if (LWCHandl == IntPtr.Zero) return new int();
            return (LW_SetJoyAPIType(LWCHandl, ApiType));
        }

        public int SetJoyAPIType(VX_JOY_API_TYPE ApiType)
            => SetJoyAPIType((int)ApiType);

        public int GetJoyAPIType()
        {
            if (LWCHandl == IntPtr.Zero) return new int();
            return (LW_GetJoyAPIType(LWCHandl));
        }

        public int SetJoyDeadZone(float newJoyDeadzone)
        {
            if (LWCHandl == IntPtr.Zero) return new int();
            return (LW_SetJoyDeadZone(LWCHandl, newJoyDeadzone));
        }

        public float GetJoyDeadZone()
        {
            if (LWCHandl == IntPtr.Zero) return new float();
            return (LW_GetJoyDeadZone(LWCHandl));
        }

        public int GetJoyCount()
        {
            if (LWCHandl == IntPtr.Zero) return new int();
            return (LW_GetJoyCount(LWCHandl));
        }

        public int ReplaceJoyInputStruct(int joyID, vxl_joy_t joyData)
        {
            if (LWCHandl == IntPtr.Zero) return new int();
            return (LW_ReplaceJoyInputStruct(LWCHandl, joyID, joyData));
        }

        public vxl_joy_t GetJoyRawInput(int joyID)
        {
            if (LWCHandl == IntPtr.Zero) return new vxl_joy_t();
            return (LW_GetJoyRawInput(LWCHandl, joyID));
        }

        public vxl_joy_t GetJoyRawPrevInpu(int joyID)
        {
            if (LWCHandl == IntPtr.Zero) return new vxl_joy_t();
            return (LW_GetJoyRawPrevInput(LWCHandl, joyID));
        }

        public float GetJoyAxisValue(int joyID, int joyAxisCode)
        {
            if (LWCHandl == IntPtr.Zero) return new float();
            return (LW_GetJoyAxisValue(LWCHandl, joyID, joyAxisCode));
        }

        public float GetJoyAxisValue(int joyID, VX_JOY_AXIS_CODES joyAxisCode)
            => GetJoyAxisValue(joyID, (int)joyAxisCode);

        public point2d GetJoyAxisValueP2D(int joyID, int joyAxisCode)
        {
            if (LWCHandl == IntPtr.Zero) return new point2d();
            return (LW_GetJoyAxisValueP2D(LWCHandl, joyID, joyAxisCode));
        }

        public point2d GetJoyAxisValueP2D(int joyID, VX_JOY_AXIS_CODES joyAxisCode)
            => GetJoyAxisValueP2D(joyID, (int)joyAxisCode);

        public int GetJoyButtonState(int joyID)
        {
            if (LWCHandl == IntPtr.Zero) return new int();
            return (LW_GetJoyButtonState(LWCHandl, joyID));
        }

        public int GetJoyPrevButtonState(int joyID)
        {
            if (LWCHandl == IntPtr.Zero) return new int();
            return (LW_GetJoyPrevButtonState(LWCHandl, joyID));
        }


        public int GetJoyButtonIsDown(int joyID, int joyButtonCode)
        {
            if (LWCHandl == IntPtr.Zero) return new int();
            return (LW_GetJoyButtonIsDown(LWCHandl, joyID, joyButtonCode));
        }

        public int GetJoyButtonIsDown(int joyID, VX_JOY_BUTTON_CODES joyButtonCode)
            => GetJoyButtonIsDown(joyID, (int)joyButtonCode);


        public int GetJoyButtonOnDown(int joyID, int joyButtonCode)
        {
            if (LWCHandl == IntPtr.Zero) return new int();
            return (LW_GetJoyButtonOnDown(LWCHandl, joyID, joyButtonCode));
        }

        public int GetJoyButtonOnDown(int joyID, VX_JOY_BUTTON_CODES joyButtonCode)
            => GetJoyButtonOnDown(joyID, (int)joyButtonCode);

        public int GetJoyButtonOnUp(int joyID, int joyButtonCode)
        {
            if (LWCHandl == IntPtr.Zero) return new int();
            return (LW_GetJoyButtonOnUp(LWCHandl, joyID, joyButtonCode));
        }

        public int GetJoyButtonOnUp(int joyID, VX_JOY_BUTTON_CODES joyButtonCode)
            => GetJoyButtonOnUp(joyID, (int)joyButtonCode);


        public float GetJoyTriggerValue(int joyID, int joyTriggerCode)
        {
            if (LWCHandl == IntPtr.Zero) return new float();
            return (LW_GetJoyTriggerValue(LWCHandl, joyID, joyTriggerCode));
        }

        public float GetJoyTriggerValue(int joyID, VX_JOY_TRIGGER_CODES joyTriggerCode)
            => GetJoyTriggerValue(joyID, (int)joyTriggerCode);


        public float GetJoyOrientation(int joyID)
        {
            if (LWCHandl == IntPtr.Zero) return new float();
            return (LW_GetJoyOrientation(LWCHandl, joyID));
        }

        public int SetJoyOrientation(int joyID, float orientationDegrees)
        {
            if (LWCHandl == IntPtr.Zero) return new int();
            return (LW_SetJoyOrientation(LWCHandl, joyID, orientationDegrees));
        }

        public int SetJoyVibration(int joyID, float leftMotorSpeed, float rightMotorSpeed)
        {
            if (LWCHandl == IntPtr.Zero) return new int();
            return (LW_SetJoyVibration(LWCHandl, joyID, leftMotorSpeed, rightMotorSpeed));
        }

        public int GetJoyAxisInversion(int joyID, int axisCode)
        {
            if (LWCHandl == IntPtr.Zero) return new int();
            return (LW_GetJoyAxisInversion(LWCHandl, joyID, axisCode));
        }

        public int GetJoyAxisInversion(int joyID, VX_JOY_AXIS_CODES axisCode)
            => GetJoyAxisInversion(joyID, (int)axisCode);

        public void SetJoyAxisInversion(int joyID, int axisCode, int value)
        {
            if (LWCHandl == IntPtr.Zero) return;
            LW_SetJoyAxisInversion(LWCHandl, joyID, axisCode, value);
        }

        public void SetJoyAxisInversion(int joyID, VX_JOY_AXIS_CODES axisCode, int value)
            => SetJoyAxisInversion(joyID, (int)axisCode, value);

        // 3rd Party Libraries

        public int ExtKlibKpzload(string filePath, ref tiletype TileToPopulate)
        {
            if (LWCHandl == IntPtr.Zero) return -1;
            return LW_EXT_KPLIB_KPZLOAD(filePath, ref TileToPopulate);
        }

        // Led Host Extended Functions

        public void VxlMath_RotateVectors(float amount, ref point3d a, ref point3d b, bool useDegrees)
        {
            if (LWCHandl == IntPtr.Zero) return;
            LW_RotVec(amount, ref a, ref b, useDegrees ? 1 : 0);
        }

        public bool VxlMath_TransformCoordinates(int FROM, int TO, ref point3d values)
        {
            if (LWCHandl == IntPtr.Zero) return false;
            return LW_VxlMath_TransformCoordinates(FROM, TO, ref values);
        }
        public float VxlMath_TransformGetAxis(int WorldDirection, int CoordinateSystem, ref point3d value)
        {
            if (LWCHandl == IntPtr.Zero) return 0;
            return LW_VxlMath_TransformGetAxis(WorldDirection, CoordinateSystem, ref value);
        }

        #endregion
    }
}
