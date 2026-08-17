
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Voxon
{
    #region public_enums

    // LEDWIN / LEDHOST enums

    public enum VXL_FLAGS { VSFLAGS_GOTFT60X = 1, VSFLAGS_GOTVSPIN = 2 };

    public enum LW_FLAGS
    {
        LW_FLAG_DRAWBORDER = 0,     //!< Toggle Border
        LW_FLAG_DEBUG = 1,          //!< Show Debug Messages for Window
        LW_FLAG_USE_DD_BUFFER = 2,  //!< Use DirectDrawBuffer - stores all LedWin's 2D graphic calls and executes them StopDriectDraw()  --- needed for CS Runtime
        LW_FLAG_EXCLUSIVE_LED = 3,  //!< Optimisation When Display is running don't displat on the 2D view...
    };

    /// <summary>
    /// Enum for VXU Bridge flags, used to control various features of the VXU Bridge interface.
    /// </summary>
    public enum VXU_FLAG_IDS
    {
        VXUB_FLAG_DISABLE_LOGGING = 0,           //!< If enabled, the VXU_Bridge.log does not get written.
        VXUB_FLAG_DONTFREE_HANDLE_ON_EXIT = 1,	 //!< If enabled, the LEDWINCS handle will not be freed on <c>Runtime.Unload()</c>. This helps the DLL integrate with Unity.
        VXUB_FLAG_BYPASS_PATH_ENV_DLL_CHECK = 2, //!< Disables looking for the DLLs in the Path environment.
        VXUB_FLAG_BYPASS_REGISTRY_DLL_CHECK = 3  //!< Disables looking for the DLLs in the registry environment.
    }

    /// <summary>
    /// These are used for transfroming AXISES between different 3D systems.
    /// 
    /// </summary>
    public enum LedWinCoordinateSystemsTransforms
    {
        LW_COORDINATE_LEFT_HANDED_Z_UP_AND_INVERTED = 0,    //<! (Left Handed Z Up & Inverted) (Voxon Native)		: WR X+, WF Y-, WU Z-
        LW_COORDINATE_VOXON_SYSTEM = 0,                     //<! 3D Coordinate System For Voxon's Native Software	: LHZI, L WR X+, WF Y-, WU Z-

        LW_COORDINATE_LEFT_HANDED_Y_UP = 1,                 //<! (Left Handed Y Up ) (Unity)		: WR X+, WF Z+, WU Y+
        LW_COORDINATE_UNITY_SYSTEM = 1,                     //<! 3D Coordinate System for Unity		: LHY :  WR X+, WF Z+, WU Y+
        LW_COORDINATE_ZBRUSH_SYSTEM = 1,                    //<! 3D Coordinate System for ZBrush	: LHY :  WR X+, WF Z+, WU Y+

        LW_COORDINATE_LEFT_HANDED_Z_UP = 2,                 //<! (Left Handed Z Up ) (Unreal)				: WR X+, WF Y-, WU Z+
        LW_COORDINATE_UNREAL_SYSTEM = 2,                    //<! 3D Coordinate System For Unreal Engine		: LHZ  : WR X+, WF Y-, WU Z+

        LW_COORDINATE_RIGHT_HANDED_Y_UP = 3,                //<! (Right Handed Y Up ) (Maya)	: WR X+, WF Z-, WU Y+
        LW_COORDINATE_MAYA_SYSTEM = 3,                      //<! 3D Coordinate System for Maya	: RHY : WR X+, WF Z-, WU Y+
        LW_COORDINATE_GODOT_SYSTEM = 3,                     //<! 3D Coordinate System for GODOT : RHY : WR X+, WF Z-, WU Y+

        LW_COORDINATE_RIGHT_HANDED_Z_UP = 4,                //<! (Right Handed Z Up ) (Blender)		: WR X+, WF Y+, WU Z+
        LW_COORDINATE_BLENDER_SYSTEM = 4,                   //<! 3D Coordinate System for Blender	: RHZ : WR X+, WF Y+, WU Z+
        LW_COORDINATE_MAX3D_SYSTEM = 4,                     //<! 3D Coordinate System for 3DS MAX	: RHZ : WR X+, WF Y+, WU Z+

        LW_COORDINATE_SYSTEM_MAX_VALUE = 5,                 //<! MAX VALUE of Implemented Coordinate System

    };



    public enum MENU_TYPE
    {

        MENU_TEXT = 0,                                  //!<  text (decoration only)
        MENU_LINE = 1,                                  //!<  line (decoration only)
        MENU_BUTTON_MIDDLE = 2,                         //!<  push button in the middle of a group
        MENU_BUTTON_FIRST = MENU_BUTTON_MIDDLE + 1,     //!<  push button first in the group
        MENU_BUTTON_LAST = MENU_BUTTON_MIDDLE + 2,      //!<  push button last in the group
        MENU_BUTTON_SINGLE = MENU_BUTTON_MIDDLE + 3,    //!<  push button single button
        MENU_HSLIDER = MENU_BUTTON_MIDDLE + 4,          //!<  horizontal slider
        MENU_VSLIDER = 7,                               //!<  vertical slider
        MENU_EDIT = 8,                                  //!<  edit text box
        MENU_EDIT_DO = 9,                               //!<  edit text box + activates next item on 'Enter'
        MENU_TOGGLE = 10,                               //!<  combo box (w/o the drop down). Useful for saving space in dialog.
                                                        //!<  Specify multiple strings in 'st' using \r as separator.
        MENU_PICKFILE = 11                              //!<  File selector. Specify type in 2nd string. Example "Browse\r*.kv6"
    };

    // Voxon and Vxl Keyboard scancodes
    public enum VX_KEYS
    {
        KB_ = 0x00,                     //!< 0x00 Empty state 
        KB_Escape = 0x01,               //!< 0x01 'Esc' / Escape key

        // Numeric Keys

        KB_1 = 0x02,                    //!< 0x02 Number 1 key
        KB_2 = 0x03,                    //!< 0x03 Number 2 key
        KB_3 = 0x04,                    //!< 0x04 Number 3 key
        KB_4 = 0x05,                    //!< 0x05 Number 4 key
        KB_5 = 0x06,                    //!< 0x06 Number 5 key
        KB_6 = 0x07,                    //!< 0x07 Number 6 key
        KB_7 = 0x08,                    //!< 0x08 Number 7 key
        KB_8 = 0x09,                    //!< 0x09 Number 8 key
        KB_9 = 0x0A,                    //!< 0x0A Number 9 key
        KB_0 = 0x0B,                    //!< 0x0B Number 0 key

        // Alpha Keys

        KB_A = 0x1E,                    //!< 0x1E 'A' key
        KB_B = 0x30,                    //!< 0x30 'B' key
        KB_C = 0x2E,                    //!< 0x2E 'C' key
        KB_D = 0x20,                    //!< 0x20 'D' key
        KB_E = 0x12,                    //!< 0x12 'E' key
        KB_F = 0x21,                    //!< 0x21 'F' key
        KB_G = 0x22,                    //!< 0x22 'G' key
        KB_H = 0x23,                    //!< 0x23 'H' key
        KB_I = 0x17,                    //!< 0x17 'I' key
        KB_J = 0x24,                    //!< 0x24 'J' key
        KB_K = 0x25,                    //!< 0x25 'K' key
        KB_L = 0x26,                    //!< 0x26 'L' key
        KB_M = 0x32,                    //!< 0x32 'M' key
        KB_N = 0x31,                    //!< 0x31 'N' key
        KB_O = 0x18,                    //!< 0x18 'O' key
        KB_P = 0x19,                    //!< 0x19 'P' key
        KB_Q = 0x10,                    //!< 0x10 'Q' key
        KB_R = 0x13,                    //!< 0x13 'R' key
        KB_S = 0x1F,                    //!< 0x1F 'S' key
        KB_T = 0x14,                    //!< 0x14 'T' key
        KB_U = 0x16,                    //!< 0x16 'U' key
        KB_V = 0x2F,                    //!< 0x2F 'V' key
        KB_W = 0x11,                    //!< 0x11 'W' key
        KB_X = 0x2D,                    //!< 0x2D 'X' key
        KB_Y = 0x15,                    //!< 0x15 'Y' key
        KB_Z = 0x2C,                    //!< 0x2C 'Z' key

        // Special Keys

        KB_Alt_Left = 0x38,             //!< 0x38 Left Alt
        KB_Alt_Right = 0xB8,            //!< 0xB8 Right Alt  
        KB_Backspace = 0x0E,            //!< 0x0E Backspace
        KB_CapsLock = 0x3A,             //!< 0x3A Capslock
        KB_Comma = 0x33,                //!< 0x33 Comma
        KB_Control_Left = 0x1D,         //!< 0x1D Left Control
        KB_Control_Right = 0x9D,        //!< 0x9D Right Control
        KB_Delete = 0xD3,               //!< 0xD3 Delete
        KB_Forward_Slash = 0x35,        //!< 0x35 Divide
        KB_Full_Stop = 0x34,            //!< 0x34 Fullstop / Greater Than
        KB_End = 0xCF,                  //!< 0xCF End
        KB_Enter = 0x1C,                //!< 0x1C Enter
        KB_Equals = 0x0D,               //!< 0x0D Equals
        KB_Home = 0xC7,                 //!< 0xC7 Home
        KB_Insert = 0xD2,               //!< 0xD2 Insert
        KB_Minus = 0x0C,                //!< 0x0C Minus / Dash 
        KB_Numlock = 0xC5,              //!< 0xC5 Numlock
        KB_Page_Down = 0xD1,            //!< 0xD1 Page Down
        KB_Page_Up = 0xC9,              //!< 0xC9 Page Up
        KB_Pause = 0x45,                //!< 0x45 Pause
        KB_Print_Screen = 0xB7,         //!< 0xB7 Print_Screen
        KB_Secondary_Action = 0xDD,     //!< 0XDD Secondary Action (not on all keyboards)
        KB_Semicolon = 0x27,            //!< 0x27 Semicolon / Less Than
        KB_ScrollLock = 0x46,           //!< 0x46 Scrolllock
        KB_Shift_Left = 0x2A,           //!< 0x2A Left Shift
        KB_Shift_Right = 0x36,          //!< 0x36 Right Shift
        KB_Single_Quote = 0x28,         //!< 0x28 Single Quote / Double Quote
        KB_Space_Bar = 0x39,            //!< 0x39 Spacebar
        KB_Square_Bracket_Open = 0x1A,  //!< 0x1A Open Square Bracket 
        KB_Square_Bracket_Close = 0x1B, //!< 0x1B Closed Square Bracket
        KB_Tab = 0x0F,                  //!< 0x0F Tab
        KB_Tilde = 0x29,                //!< 0x29 Tilde
        KB_Back_Slash = 0x2B,           //!< 0x2B BackSlash (Used by VX1 to open menu)

        // Function Keys

        KB_F1 = 0x3B,                   //!< 0x3B F1 function key
        KB_F2 = 0x3C,                   //!< 0x3C F2 function key
        KB_F3 = 0x3D,                   //!< 0x3D F3 function key
        KB_F4 = 0x3E,                   //!< 0x3E F4 function key
        KB_F5 = 0x3F,                   //!< 0x3F F5 function key
        KB_F6 = 0x40,                   //!< 0x40 F6 function key
        KB_F7 = 0x41,                   //!< 0x41 F7 function key
        KB_F8 = 0x42,                   //!< 0x42 F8 function key
        KB_F9 = 0x43,                   //!< 0x43 F9 function key
        KB_F10 = 0x44,                  //!< 0x44 F10 function key
        KB_F11 = 0x57,                  //!< 0x57 F11 function key
        KB_F12 = 0x58,                  //!< 0x58 F12 function key

        // Numpad

        KB_NUMPAD_0 = 0x52,             //!< 0x52 Numpad 0 key
        KB_NUMPAD_1 = 0x4F,             //!< 0x4F Numpad 1 key
        KB_NUMPAD_2 = 0x50,             //!< 0x50 Numpad 2 key	
        KB_NUMPAD_3 = 0x51,             //!< 0x51 Numpad 3 key
        KB_NUMPAD_4 = 0x4B,             //!< 0x4B Numpad 4 key
        KB_NUMPAD_5 = 0x4C,             //!< 0x4C Numpad 5 key
        KB_NUMPAD_6 = 0x4D,             //!< 0x4D Numpad 6 key
        KB_NUMPAD_7 = 0x47,             //!< 0x47 Numpad 7 key
        KB_NUMPAD_8 = 0x48,             //!< 0x48 Numpad 8 key
        KB_NUMPAD_9 = 0x49,             //!< 0x49 Numpad 9 key
        KB_NUMPAD_Decimal = 0x53,       //!< 0x53 Numpad decimal key
        KB_NUMPAD_Divide = 0xB5,        //!< 0xB5 Numpad divide key
        KB_NUMPAD_Multiply = 0x37,      //!< 0x37 Numpad multiply key
        KB_NUMPAD_Minus = 0x4A,         //!< 0x4A Numpad minus key
        KB_NUMPAD_Plus = 0x4E,          //!< 0x4E Numpad plus key
        KB_NUMPAD_Enter = 0x9C,         //!< 0x9C Numpad enter key

        // Arrow Keys

        KB_Arrow_Right = 0xCD,          //!< 0xCD Arrow Right
        KB_Arrow_Left = 0xCB,           //!< 0xCB Arrow Left
        KB_Arrow_Up = 0xC8,             //!< 0xC8 Arrow Up
        KB_Arrow_Down = 0xD0,           //!< 0xD0 Arrow Down


    };


    public enum VX_JOY_BUTTON_CODES
    {
        JOY_DPAD_UP = 0,    //!< bit 0 value 1			Digital Dpad Up				
        JOY_DPAD_DOWN = 1,  //!< bit 1 value 2			Digital Dpad Down 
        JOY_DPAD_LEFT = 2,  //!< bit 2 value 4			Digital Dpad Left
        JOY_DPAD_RIGHT = 3, //!< bit 3 value 8			Digital Dpad Right
        JOY_START = 4,  //!< bit 4 value 16			Start Button
        JOY_BACK = 5,   //!< bit 5 value 32			Back Button
        JOY_LEFT_THUMB = 6, //!< bit 6 value 64			Left Thumb Stick Button (when you press 'down' on the left analog stick)
        JOY_RIGHT_THUMB = 7,    //!< bit 7 value 128		Right Thumb Stick Button (when you press 'down' on the right analog stick)
        JOY_LEFT_SHOULDER = 8,  //!< bit 8 value 256		Left Shoulder Bumper Button - not the Shoulder triggers are analog 
        JOY_RIGHT_SHOULDER = 9, //!< bit 9 value 512		Right Shoulder Bumper Button - not the Shoulder triggers are analog 
        JOY_A = 12, //!< bit 12 value 1,024		The 'A' Button on a standard Xbox Controller
        JOY_B = 13, //!< bit 13 value 2,048		The 'B' Button on a standard Xbox Controller
        JOY_X = 14, //!< bit 14 value 4,096		The 'X' Button on a standard Xbox Controller
        JOY_Y = 15  //!< bit 15 value 8,192		The 'Y' Button on a standard Xbox Controller

    };
    //! \enum Controller Axis Enum helps identify which analog input is being used 

    public enum VX_JOY_AXIS_CODES
    {
        JOY_AXIS_LEFT_STICK_X,           //!< 0 Analog Left Stick X axis
        JOY_AXIS_LEFT_STICK_Y,           //!< 1 Analog Left Stick Y axis
        JOY_AXIS_RIGHT_STICK_X,          //!< 2 Analog Right Stick X axis
        JOY_AXIS_RIGHT_STICK_Y,          //!< 3 Analog Right Stick Y axis
        JOY_AXIS_LEFT_TRIGGER = 4,       //!< 4 Analog Left Shoulder trigger 
        JOY_AXIS_RIGHT_TRIGGER = 5,      //!< 5 Analog Right Shoulder trigger
    };


    public enum VX_JOY_TRIGGER_CODES
    {
        JOY_LEFT_TRIGGER = 4,       //!< 5 Analog Left Trigger 
        JOY_RIGHT_TRIGGER = 5,      //!< 6 Analog Right Trigger 
    };


    //! \enum the input codes for mouse and SpaceNav buttons 
    public enum VX_MOUSE_BUTTON_CODES
    {
        MOUSE_LEFT_BUTTON,          //!< bit 0 value 1			Left Mouse Button
        MOUSE_RIGHT_BUTTON,         //!< bit 1 value 2			Right Mouse Button
        MOUSE_MIDDLE_BUTTON,         //!< bit 2 value 4			Middle Mouse Button
        MOUSE_BUTTON_LEFT = 0,          //!< bit 0 value 1			Left Mouse Button
        MOUSE_BUTTON_RIGHT = 1,         //!< bit 1 value 2			Right Mouse Button
        MOUSE_BUTTON_MIDDLE = 2,        //!< bit 2 value 4			Middle Mouse Button
        MOUSE_BUTTON_EXTRA = 3,         //!< bit 3 value 8			extra Mouse Button

    };

    public enum VX_MOUSE_LOCATION_CODES
    {
        MOUSE_OUTSIDE_WINDOW = 0,		//!< Mouse pointer is in Window Menu
        MOUSE_INSIDE_WINDOW = 1,		//!< Mouse pointer is inside Window
        MOUSE_ONTITLEBAR_WINDOW = 2,    //!< Mouse pointer is on the titlebar
    };

    //! \enum the input codes for mouse and SpaceNav buttons 
    public enum NavButton
    {
        NAV_LEFT_BUTTON,            //!< bit 0 value 1 Left  SpaceNav Button
        NAV_RIGHT_BUTTON,           //!< bit 1 value 2 Right SpaceNav Button
    };

    public enum NavAxis
    {
        NAV_X_AXIS,         //!< 0 Left SpaceNav Button
        NAV_Y_AXIS,         //!< 1 RightSpaceNav Button
        NAV_Z_AXIS
    };


    //! \enum the input codes for mouse and SpaceNav buttons 
    public enum VX_NAV_BUTTON_CODES
    {
        NAV_LEFT_BUTTON,            //!< bit 0 value 1 Left  SpaceNav Button
        NAV_RIGHT_BUTTON,           //!< bit 1 value 2 Right SpaceNav Button
    };

    public enum VX_NAV_AXIS_CODES
    {
        NAV_X_AXIS_SUMMED,
        NAV_Y_AXIS_SUMMED,
        NAV_Z_AXIS_SUMMED,
        NAV_X_AXIS_DIRECTION,
        NAV_Y_AXIS_DIRECTION,
        NAV_Z_AXIS_DIRECTION,
        NAV_X_AXIS_ANGLE,
        NAV_Y_AXIS_ANGLE,
        NAV_Z_AXIS_ANGLE,
        NAV_PITCH_AXIS_ANGLE = NAV_X_AXIS_ANGLE,
        NAV_YAW_AXIS_ANGLE = NAV_Y_AXIS_ANGLE,
        NAV_ROLL_AXIS_ANGLE = NAV_Z_AXIS_ANGLE,
    };

    public enum VX_JOY_API_TYPE
    {

        VX_JOY_API_DIRECT_INPUT = 0,              // For older Controllers - Direct Input for controllers
        VX_JOY_API_X_INPUT = 1,                  // For XBox and Modern style controllers - X Input
    };

    #endregion

    #region public_structures
    /// <summary>
    /// 3D Vector type
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct point3d
    {
        /// <summary>
        /// Position in 3 Dimensional space
        /// </summary>
        public float x, y, z;

        /// <summary>
        /// Get the current position
        /// </summary>
        /// <returns>Position in format [x,y,z]</returns>
        public float[] GetPosition()
        {
            return new float[] { x, y, z };
        }

        /// <summary>
        /// Assign a new position
        /// </summary>
        /// <param name="pos">Position in format [x,y,z]</param>
        public point3d(float[] pos)
        {
            x = pos[0];
            y = pos[1];
            z = pos[2];
        }

        /// <summary>
        /// Assign a new point via floats
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="z"></param>
        public point3d(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        /// <summary>
        /// Construct a point3d from a poltex
        /// </summary>
        /// <param name="pos">A poltex with position parameter assigned</param>
        public point3d(poltex pos)
        {
            x = pos.x;
            y = pos.y;
            z = pos.z;
        }

        /// <summary>
        /// Generate a string representation of the position
        /// </summary>
        /// <returns>Position in form "(x,y,z)"</returns>
        public override string ToString()
        {
            return "(" + x + "," + y + "," + z + ")";
        }

        public static point3d operator +(point3d p1, point3d p2)
        {
            return new point3d(p1.x + p2.x, p1.y + p2.y, p1.z + p2.z);
        }

        public static point3d operator -(point3d p1, point3d p2)
        {
            return new point3d(p1.x - p2.x, p1.y - p2.y, p1.z - p2.z);
        }

        public static point3d operator *(point3d p1, point3d p2)
        {
            return new point3d(p1.x * p2.x, p1.y * p2.y, p1.z * p2.z);
        }

        public static point3d operator /(point3d p1, point3d p2)
        {
            return new point3d(p1.x / p2.x, p1.y / p2.y, p1.z / p2.z);
        }


    }

    ///
    /// Triangle
    /// </summary>
    public struct tri_t
    {
        public float x0, y0, z0;
        public int n0;
        public float x1, y1, z1;
        public int n1;
        public float x2, y2, z2;
        public int n2;
    }

    // input_extended
    public struct input_ext
    {
        public int[] nav_obut;
        public Dictionary<int, double> oKeys; // Keycode and timeheld. 

    }


    // Controls
    /// <summary>
    /// XBox controller state covering current input, offset, and last frame input
    /// </summary>
    public struct xbox_input
    {
        public voxie_xbox_t input;
        public voxie_xbox_t offset;
        public voxie_xbox_t last_frame;
    }

    /// <summary>
    /// Space Navigator state
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct voxie_nav_t
    {
        public float dx, dy, dz; //Space Navigator translation
        public float ax, ay, az; //Space Navigator rotation
        public int but; //Space Navigator button status

    };


    /// <summary>
    /// 2D Vector representation
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct point2d
    {
        public float x, y;
    }


    /// <summary>
    /// Position with color component
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct pol_t
    {
        public float x, y, z;
        public int p2;
    }

    /// <summary>
    /// Full vertice data, covers position, uv and color components
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct poltex
    {
        /// <summary>
        /// Position and UV Values
        /// </summary>
        public float x, y, z, u, v;
        /// <summary>
        /// Color represented as integer
        /// </summary>
        public int col;

        /// <summary>
        /// String representation of poltex
        /// </summary>
        /// <returns>Returns string in format "(x,y,z)-(u,v) : col"</returns>
        override public string ToString()
        {
            return "(" + x + ", " + y + ", " + z + ")-(" + u + ", " + v + ") : " + col;
        }

        /// <summary>
        /// Get the current position
        /// </summary>
        /// <returns>Returns position in format [x,y,z]</returns>
        public float[] GetPosition()
        {
            return new float[] { x, y, z };
        }

        /// <summary>
        /// Set poltex from point3d Position. Default u,v,col to 0
        /// </summary>
        /// <param name="pos">Position</param>
        public poltex(point3d pos)
        {
            x = pos.x;
            y = pos.y;
            z = pos.z;
            u = 0;
            v = 0;
            col = 0;
        }
    }

    /// <summary>
    /// Texture representation
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct tiletype
    {
        /// <summary>
        /// pointer to first pixel of the texture (usually the top-left corner)
        /// </summary>
        public nint first_pixel;
        /// <summary>
        /// number of bytes per horizontal line (usually x*4)
        /// </summary>
        public nint pitch;
        /// <summary>
        /// width & height of texture
        /// </summary>
        public nint width, height;
    }

    /// <summary>
    ///  extents used for drawing 3d model sequences
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct extents_t
    {
        public float x0, y0, z0, sc, x1, y1, z1, dum;

    }


    #endregion

    #region	 Voxon VLED (Vxl) Engine structs


    /// <summary>
    ///  camera struct to manage a virtual viewport / camera 
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct cam_t
    {
        /* ref */
        tiletype dd; // TODO can't use a ref in struct 
        /// <summary>
        /// p = position
        /// </summary>
        point3d p;

        /// <summary>
        /// r = right vector
        /// </summary>
        point3d r;

        /// <summary>
        /// d = down vector
        /// </summary>
        point3d d;

        /// <summary>
        /// f = forward vector
        /// </summary>
        point3d f;

        /// <summary>
        /// h = ??? TODO
        /// </summary>
        point3d h;

    }


    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct vxl_mou_t // manages the state of the Mouse
    {
        public int xPos, yPos; //<<! the X and Y pixel position of mouse in the window
        public int deltaX, deltaY, deltaZ; //<<! delta movements from house (Z = scroll wheel
        public int butState, prevButState; //<<! the buttonstate for the mouse buttons 0 = no buttons pressed,  1 = left mouse button pressed, 2 = right mouse button pressed, 3 = both left and right button pressed. 
        public int location; // 0 = mouse is out of window,  1 mouse is in window, 2 mouse is on title bar
    }


    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct vxl_nav_t // manages the state of the SpaceNav for VLED engine
    {
        public float dx, dy, dz, ax, ay, az;
        public int but;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct vxl_joy_t // manages the state of the Joystick for VLED engine
    {
        public short but, lt, rt, tx0, ty0, tx1, ty1, hat;

    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct vxl_vpen_t // manages the state of the Voxon Vpen for VLED engine
    {
        public int x, y, z, p, b, t;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    // struct to manage a single module for a LED display
    public struct module_t
    {
        public int ind;
        public int x0;
        public int y0;
        public int xs;
        public int ys;
        public int rot;         //rot:0=horz, 1=CW 90, 2=180, 3=CCW 90
        public int reserved0;      // 0:xRGB,  1:xBGR
    }


    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct vxl_anim_t
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string filespec;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string snd;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string staticfile;

        float dtim, frameper, rplayspeed, defcale, forcescale;
        int curframe, minframe, maxframe, mode, hsnd;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct vxl_state_t // manages the state of the voxel LED engine
    {
        public int size;                // written by DLL:vxl_init(); use to detect DLL mismatch
        public short hub75per, hub75wid, hub75off, reserved0_short;
        public int ft_presc;			// FT60x clock rate: 100e6 or 66e6 / labs(ft_presc). NOTE:negative # implies DFF-XOR enabled!
        public int xsiz, ysiz, zsiz;	// Current LED resolution. X: number of horizontal LEDs, Y: number of vertical LEDs, Z: number of rotational slices to render a volume
        public int rotoffs16;			//Hack to offset rotation (0..65535) [ in the ledhost.ini this number is a degree ... to convert to an int    *(65536.0/360.0))&65535); eg for 180 degrees would be rotoffs16 = (double)180*(65536.0/360.0))&65535);
        public float offaxdist;         //distance in mm from led plane to center of axis of rotation
        public int badslicemode;        // 0=clear slice, 1=retain old slice (for missed packets)

        public uint hwbri_packed;
        public int vxmodeln;

        public int res2_0, res2_1, res2_2, res2_3;

        public int stprpm;
        public int flags;        //See VSFLAGS_* above
        public int colchip;
        public int rowtype;

        public int xres, yres;   //Requested window size -- not used for LEDWIN systems

        // Internal precalcs
        public int xysiz, xsiz4, xysiz4, xsizm1, xsizo6, xysizo6, ysizm1, zsizm1;
        public int xsizm1up5;
        public float boundr, boundr2, boundz; //World units for cropping
        public int minrpm, maxrpm, rpm; // Speed settings  minRPM, maxRPM and current RPM (default value in LedHost.ini writes to current RPM)
        public int dithmode;            // Dither mode (0 = or 1)
        public int dithresh;            // Dither threshold default is 64
        public float gammapow;          // Gamma default :2.0f
        public int drawbord;            // Draw Border - how to determine if the software should draw a border around the outside

        public int reserved3;

        public int draw3d;       //0=2D image animation mode, 1=3D spinner mode
        public int drawbilin;   //0=nearest, 1=bi-linear
        public int wireframe;      //vxl_drawpol() render mode : 0=regular, 1=wireframe

        public int rbswap;
        public int reserved4;

        public int modulen;
        // layout variables to how a C array would access them this avoids setting an array of a
        // "unsafe" fixed size. Ensure that [StructLayout(LayoutKind.Sequential, Pack = 1)] is declared
        public module_t module0, module1, module2, module3, module4, module5,
            module6, module7, module8, module9, module10, module11, module12,
            module13, module14, module15;
    }


    #endregion

    #region  public_VoxieBox Engine structs


    /// <summary>
    /// Delegate type used to construct MenuUpdate functions
    /// </summary>
    /// <param name="id"></param>
    /// <param name="st"></param>
    /// <param name="v"></param>
    /// <param name="how"></param>
    /// <param name="userdata"></param>
    /// <returns></returns>
    public delegate int MenuUpdateHandler(int id, string st, double v, int how, nint userdata);

    /// <summary>
    /// XBox controller state structure
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct voxie_xbox_t
    {
        /// <summary>
        /// XBox controller buttons (same layout as XInput)
        /// </summary>
        public short but;
        /// <summary>
        /// XBox controller left&right triggers (0..255)
        /// </summary>
        public short lt, rt;
        /// <summary>
        /// XBox controller left  joypad (-32768..32767)
        /// </summary>
        public short tx0, ty0;
        /// <summary>
        /// XBox controller right joypad (-32768..32767)
        /// </summary>
        public short tx1, ty1;
        /// <summary>
        /// Hat button values
        /// </summary>
        public short hat;
    }

    /// <summary>
    /// Representation of frame buffer
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct voxie_frame_t
    {
        public nint f;              //Pointer to top-left-up of current frame to draw
        public nint p;              //Number of bytes per horizontal line (x)
        public nint fp;             //Number of bytes per 24-plane frame (1/3 of screen)
        public int x, y;               //Width and height of viewport
        public int flags;             //Tells whether color mode is selected
        public int drawplanes;         //Tells how many planes to draw
        public int x0, y0, x1, y1;     //Viewport extents
        public float xmul, ymul, zmul; //Transform for medium and high level graphics functions..
        public float xadd, yadd, zadd; //Transform is: actual_x = passed_x * xmul + xadd
        public tiletype f2d;
    }

    /// <summary>
    /// Controller input state
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct voxie_inputs_t
    {
        public int bstat, obstat, dmousx, dmousy, dmousz;
    }

    /// <summary>
    /// Display state representation
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct voxie_disp_t
    {
        //settings for quadrilateral (keystone) compensation (use voxiedemo mode 2 to calibrate)
        public point2d keyst0, keyst1, keyst2, keyst3, keyst4, keyst5, keyst6, keyst7;
        public int colo_r, colo_g, colo_b; //initial values at startup for rgb color mode
        public int mono_r, mono_g, mono_b; //initial values at startup for mono mode
        public int mirrorx, mirrory;       //projector hardware flipping (I suggest avoiding this)
    }




    /// <summary>
    /// Internal device state control structure
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct voxie_wind_t
    {
        //Emulation
        public int useemu;             //0=Voxiebox, 1=emulation in 2D window (representative of actual view on HW), 2=emulation in 2D window (made for 2D display)
        public float emuhang;          //emulator horizontal angle (radians)
        public float emuvang;          //emulator vertical   angle (radians)
        public float emudist;          //emulator distance; minimum is 2000.0.

        //Display
        public int xdim, ydim;         //projector dimensions (912,1140)
        public int projrate;           //projector rate in Hz {60..107}
        public int framepervol;        //# projector frames per volume {1..16}. Ex:framepervol=3 at projrate=60
                                       //gives 60/3 = 20Hz volume rate
        public int usecol;             //0=mono white, 1=full color time multiplexed,
                                       //-1=red, -2=green, -3=yellow, -4=blue, -5=magenta, -6=cyan
        public int dispnum;            //number of displays to search for and use (typically 1)
        public int bitspervol; //if enabled (nonzero), protects ledr/g/b from going too high
        public voxie_disp_t disp0, disp1, disp2; //see voxie_disp_t

        //Actuator
        public int hwsync_frame0;      //first frame offset (-1 to disable sync hw)
        public int hwsync_phase;       //high precision phase offset
        public int hwsync_amp0, hwsync_amp1, hwsync_amp2, hwsync_amp3;      //amplitude {0..65536} for each channel
        public int hwsync_pha0, hwsync_pha1, hwsync_pha2, hwsync_pha3;      //phase {0..65536} for each channel
        public int hwsync_levthresh;   //threshold ADC value for peak detection {0..1024, but typically in range: 256..512}
        public int voxie_vol;          //amplitude scale when using sine wave audio output {0..100}

        //Render
        public int ilacemode;
        public int drawstroke;         //1=draw on up stroke, 2=draw on down stroke, 3=draw on both up&down
        public int dither;             //0=dither off, 1=dither mono only, 2=dither RGB only, 3=dither both
        public int smear;              //1+=increase brightness in x-y at post processing (slower render), 0=not
        public int usekeystone;        //0=no keystone compensation (for testing only), 1=keystone quadrilateral compensation (default) - see proj[]
        public int flip;               //flip coordinate system in voxiebox.dll
        public int menu_on_voxie;      //1=display menu on voxiebox view

        public float aspx, aspy, aspz; //aspect ratio, loaded from voxiebox.ini, used by application. Values typically around 1.f
        public float gamma;            //gamma value for interpolating in voxie_drawspr()/voxie_drawheimap().
        public float density;          //scale factor controlling dot density of STL model rendering (default:1.f)

        //Audio
        public int sndfx_vol;          //amplitude scale of sound effects {0..100}
        public int voxie_aud;          //audio channel index of motor. (Playback devices..Speakers..Configure..
                                       //   Audio Channels)
        public int excl_audio;         //1=exclusive audio mode (much faster & more stable sync - recommended!
                                       //0=shared audio mode (if audio access to other programs required)
        public int sndfx_aud0, sndfx_aud1;       //audio channel indices of sound effects, [0]=left, [1]=right
        public int playsamprate;       //sample rate used by audio driver (written by voxie_init()). 0 is written
                                       //   if no audio channels are enabled in voxiebox.ini.
        public int playnchans;         //number of audio channels expected to be rendered by user audio mixer
        public int recsamprate;        //recording sample rate - to use, must write before voxie_init()
        public int recnchans;          //number of audio channels in recording callback

        //Misc.
        public int isrecording;        //0=normally, 1 when .REC file recorder is in progress (written by DLL)
        public int hacks;              //1=exclusive mouse, 0=not
        public int dispcur;            //current display selected in menus {0..dispnum-1}
        public int sndfx_nspk;
        public int reserved;

        //Obsolete
        public float freq;            //starting value in Hz (must be set before first call to voxie_init()); obsolete - not used by current hardware
        public float phase;           //phase lock {0.0..1.0} (can be updated on later calls to voxie_init()); obsolete - not used by current hardware

        //Helix
        int thread_override_hack; //0:default thread behavior, 1..n:force n threads for voxie_drawspr()/voxie_drawheimap(); bound to: {1 .. #CPU cores (1 less on hw)}
        public int motortyp; //0=Old DC brush motor, 1=ClearPath using Frequency Input, 2=AU brushless airplane motor
        public int clipshape; //0=rectangle (vw.aspx,vw.aspy), 1=circle (vw.aspr)
        public int goalrpm, cpmaxrpm, ianghak, ldotnum, normhax;
        public int upndow; //0=sawtooth, 1=triangle
        public int nblades; //0=VX1 (not spinner), 1=/|, 2=/|/|, ..
        public int usejoy; //-1=none, 0=joyInfoEx, 1=XInput
        public int dimcaps;
        public float emugam;
        public float asprmin;
        public float sync_usb_offset;
        public int sensemask0, sensemask1, sensemask2, outcol0, outcol1, outcol2;
        public float aspr, sawtoothrat;
    }


    //! used internally by VoxieBox class to log various button inputs this struct allows you to view the history of various inputs
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct voxie_input_state_t
    {
        public int inputCodeRaw;
        public int inputCode;
        public double startTime;
        public double duration;
        public double startLastPressed;
        public bool isHeld;
        public bool onUp;
        public int state;
    }



    #endregion

    #region Voxie_CS_Wrapper_Flags

    // Name and IDs for Runtime flags 
    public enum VX_FLAG_IDS
    {
        VXFLAG_DRAW_BOARDER = 0,
        VXFLAG_DISABLE_AUTO_GETVW_FLAG = 1,
        VXFLAG_MANUALLY_MANAGE_XBOX = 2,       // 0000 0100
        VXFLAG_MANUALLY_MANAGE_SPACENAV = 3,   // 0000 1000
        VXFLAG_MANUALLY_MANAGE_KEYBOARD = 4,  // 0001 0000
        VXFLAG_MANUALLY_MANAGE_MOUSE = 5,     // 0010 0000
        VXFLAG_MANUALLY_SET_VIEW = 6,        // 0100 0000
    }
    #endregion

    #region VLED helper classes

    /// <summary>
    /// Represents a mapping between a function name, its delegate type, and the action to assign the delegate.
    /// </summary>
    public class VoxonFunctionMapping
    {
        /// <summary>
        /// The name of the function in the DLL.
        /// </summary>
        public string FunctionName { get; set; }

        /// <summary>
        /// The type of the delegate corresponding to the function.
        /// </summary>
        public Type DelegateType { get; set; }

        /// <summary>
        /// The action to assign the delegate to a field or property.
        /// </summary>
        public Action<Delegate> AssignDelegate { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="VoxonFunctionMapping"/> class.
        /// </summary>
        /// <param name="functionName">The name of the function in the DLL.</param>
        /// <param name="delegateType">The type of the delegate corresponding to the function.</param>
        /// <param name="assignDelegate">The action to assign the delegate to a field or property.</param>
        public VoxonFunctionMapping(string functionName, Type delegateType, Action<Delegate> assignDelegate)
        {
            FunctionName = functionName;
            DelegateType = delegateType;
            AssignDelegate = assignDelegate;
        }
    }

    public class VLED_CS_Utils
    {


        public static readonly int[] VLED_PALETTE_COLORS = {
        0xFF00EE, 0xFF00DD, 0xFF00CC, 0xFF00BB, 0xFF00AA, 0xFF0099, 0xFF0088, 0xFF0077, 0xFF0066, 0xFF0055, 0xFF0044, 0xFF0033, 0xFF0022, 0xFF0011, 0xFF0000, // RED
        0xFF1100, 0xFF2200, 0xFF3300, 0xFF4400, 0xFF5500, 0xFF6600, 0xFF7700, 0xFF8800, 0xFF9900, 0xFFAA00, 0xFFBB00, 0xFFCC00, 0xFFDD00, 0xFFEE00, 0xFFFF00, // YELLOW
        0xEEFF00, 0xDDFF00, 0xCCFF00, 0xBBFF00, 0xAAFF00, 0x99FF00, 0x88FF00, 0x77FF00, 0x66FF00, 0x55FF00, 0x44FF00, 0x33FF00, 0x22FF00, 0x11FF00, 0x00FF00, // GREEN
        0x00FF11, 0x00FF22, 0x00FF33, 0x00FF44, 0x00FF55, 0x00FF66, 0x00FF77, 0x00FF88, 0x00FF99, 0x00FFAA, 0x00FFBB, 0x00FFCC, 0x00FFDD, 0x00FFEE, 0x00FFFF, // CYAN
        0x00EEFF, 0x00DDFF, 0x00CCFF, 0x00BBFF, 0x00AAFF, 0x0099FF, 0x0088FF, 0x0077FF, 0x0066FF, 0x0055FF, 0x0044FF, 0x0033FF, 0x0022FF, 0x0011FF, 0x0000FF, // BLUE
        0x1100FF, 0x2200FF, 0x3300FF, 0x4400FF, 0x5500FF, 0x6600FF, 0x7700FF, 0x8800FF, 0x9900FF, 0xAA00FF, 0xBB00FF, 0xCC00FF, 0xDD00FF, 0xEE00FF, 0xFF00FF  // MAGENTA
		};



        public static string GetVLEDFilePath(string fileName)
        {
            string filePath = "";

            // First look for local dll (in case of override)
            if (File.Exists(fileName))
            {
                filePath = $"{fileName}";
                return filePath;
            }

            // Second check the path variables
            if (filePath == "")
            {
                // Check User Path Environment 
                string[] userPaths = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User)?.Split(';');
                foreach (var path in userPaths)
                {
                    if (File.Exists(path + $"\\{fileName}"))
                    {
                        filePath = path + $"\\{fileName}";
                        break;
                    }
                }

                if (filePath != "") return filePath;

                // Check System Path Enviroment
                string[] systemPaths = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine)?.Split(';');

                foreach (var path in systemPaths)
                {
                    if (File.Exists(path + $"\\{fileName}"))
                    {
                        filePath = path + $"\\{fileName}";
                        break;
                    }
                }
                if (filePath != "") return filePath;
            }
            // Finally check the registry (only on Windows)

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Finally check the registry...
                if (filePath == "")
                {
                    RegistryKey registryKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Voxon\VLED");

                    // Check if the registry key exists and retrieve its value
                    if (registryKey != null)
                    {
                        var registryPath = registryKey.GetValue("VLED_RUNTIME_PATH") as string;

                        if (registryPath != null)
                        {
                            // Combine the registry path with the fileName
                            filePath = $"{registryPath}{fileName}";
                        }
                    }

                }
            }
            return filePath;
        }
    }

    #endregion

}