using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Voxon
{
    // Serialized settings + high-score table (one JSON file next to the exe).
    // Add your own game fields here; the load/save plumbing is reusable.
    internal sealed class SaveData
    {
        // universal visual
        public float Density { get; set; } = 2.0f;
        public float FigureDensity { get; set; } = 3.0f;
        public int   Color { get; set; } = 0x20E0C0;
        public bool  DemoMode { get; set; }

        // placeholder game controls
        public float RotSpeed { get; set; } = 0.5f;
        public float TextSize { get; set; } = 1.0f;
        public bool  ShowTitle { get; set; }

        // lighting (universal)
        public bool  UseGpu { get; set; } = true;        // GPU (ComputeSharp) lighting
        public int   LightMode { get; set; } = 2;        // 0 Flat 1 Normals 2 Spotlight
        public float AmbientBright { get; set; } = 0.15f;
        public float Exposure { get; set; } = 1.0f;
        public float SelfIllum { get; set; } = 1.0f;
        public float SpotIntensity { get; set; } = 8.0f;
        public float SpotRadius { get; set; } = 4.0f;
        public bool  Shadows { get; set; } = true;
        public float ShadowStrength { get; set; } = 0.75f;
        public float ShadowBias { get; set; } = 0.02f;
        public bool  OrbitLights { get; set; } = true;
        public float NormalStrength { get; set; } = 1.0f;
        public float NormalIntensity { get; set; } = 1.0f;
        public float LightAngle { get; set; } = 45f;

        // audio
        public bool SoundEnabled { get; set; } = true;
        public float Volume { get; set; } = 0.7f;

        // hardware
        [JsonPropertyName("simulatorProfile")]
        public int SystemN { get; set; }   // 0 = VX2, 1 = VX2-XL
        public bool MotorOn { get; set; } = true;
        public int Rpm { get; set; } = 900;
        public int DrawBilin { get; set; }
        public int DithMode { get; set; }
        public int DithThresh { get; set; } = 64;
        public bool DrawBorder { get; set; }
        public float Gamma { get; set; } = 2f;

        // high score table (descending)
        public List<int> HighScores { get; set; } = new();
    }

    internal static class Persist
    {
        static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "hellovoxel.json");
        static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };
        public static SaveData Data = new();

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var d = JsonSerializer.Deserialize<SaveData>(File.ReadAllText(FilePath));
                    if (d != null) Data = d;
                    Console.WriteLine("[persist] loaded " + FilePath);
                }
            }
            catch (Exception e) { Console.WriteLine("[persist] load failed: " + e.Message); }
            ApplyToBridge();
            PublishScores();
        }

        public static void Save()
        {
            try
            {
                CaptureFromBridge();
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Data, Opts));
                Console.WriteLine("[persist] saved " + FilePath);
            }
            catch (Exception e) { Console.WriteLine("[persist] save failed: " + e.Message); }
        }

        public static void AddScore(int score)
        {
            if (score <= 0) return;
            Data.HighScores.Add(score);
            Data.HighScores.Sort((a, b) => b.CompareTo(a));
            if (Data.HighScores.Count > 10) Data.HighScores.RemoveRange(10, Data.HighScores.Count - 10);
            PublishScores();
            Save();
        }

        static void PublishScores() => Bridge.HighScores = Data.HighScores.ToArray();

        static int C(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

        static void ApplyToBridge()
        {
            Bridge.Density = Data.Density <= 0 ? 2f : Data.Density;
            Bridge.FigureDensity = Data.FigureDensity <= 0 ? 3f : Data.FigureDensity;
            Bridge.Color = Data.Color == 0 ? 0x20E0C0 : Data.Color;
            Bridge.DemoMode = Data.DemoMode;
            Bridge.RotSpeed = Data.RotSpeed;
            Bridge.TextSize = Data.TextSize <= 0 ? 1f : Data.TextSize;
            Bridge.ShowTitle = Data.ShowTitle;
            Bridge.UseGpu = Data.UseGpu;
            Bridge.LightMode = C(Data.LightMode, 0, 2);
            Bridge.AmbientBright = Data.AmbientBright;
            Bridge.Exposure = Data.Exposure;
            Bridge.SelfIllum = Data.SelfIllum;
            Bridge.SpotIntensity = Data.SpotIntensity;
            Bridge.SpotRadius = Data.SpotRadius <= 0 ? 4f : Data.SpotRadius;
            Bridge.Shadows = Data.Shadows;
            Bridge.ShadowStrength = Data.ShadowStrength;
            Bridge.ShadowBias = Data.ShadowBias <= 0f ? 0.02f : Data.ShadowBias;
            Bridge.OrbitLights = Data.OrbitLights;
            Bridge.NormalStrength = Data.NormalStrength;
            Bridge.NormalIntensity = Data.NormalIntensity;
            Bridge.LightAngle = Data.LightAngle;
            Bridge.SoundEnabled = Data.SoundEnabled;
            Bridge.Volume = Data.Volume;
            Bridge.System = Data.SystemN == 0 ? 0 : 1;
            Bridge.SystemDirty = true;
            Bridge.MotorOn = Data.MotorOn;
            Bridge.Rpm = Data.Rpm <= 0 ? 900 : Data.Rpm;
            Bridge.DrawBilin = Data.DrawBilin;
            Bridge.DithMode = Data.DithMode;
            Bridge.DithThresh = C(Data.DithThresh, 0, 255);
            Bridge.DrawBorder = Data.DrawBorder;
            Bridge.Gamma = Data.Gamma <= 0 ? 2f : Data.Gamma;
        }

        static void CaptureFromBridge()
        {
            Data.Density = Bridge.Density; Data.FigureDensity = Bridge.FigureDensity;
            Data.Color = Bridge.Color; Data.DemoMode = Bridge.DemoMode;
            Data.RotSpeed = Bridge.RotSpeed; Data.TextSize = Bridge.TextSize; Data.ShowTitle = Bridge.ShowTitle;
            Data.UseGpu = Bridge.UseGpu;
            Data.LightMode = Bridge.LightMode; Data.AmbientBright = Bridge.AmbientBright; Data.Exposure = Bridge.Exposure;
            Data.SelfIllum = Bridge.SelfIllum; Data.SpotIntensity = Bridge.SpotIntensity; Data.SpotRadius = Bridge.SpotRadius;
            Data.Shadows = Bridge.Shadows; Data.ShadowStrength = Bridge.ShadowStrength; Data.ShadowBias = Bridge.ShadowBias; Data.OrbitLights = Bridge.OrbitLights;
            Data.NormalStrength = Bridge.NormalStrength; Data.NormalIntensity = Bridge.NormalIntensity; Data.LightAngle = Bridge.LightAngle;
            Data.SoundEnabled = Bridge.SoundEnabled; Data.Volume = Bridge.Volume;
            Data.SystemN = Bridge.System; Data.MotorOn = Bridge.MotorOn; Data.Rpm = Bridge.Rpm;
            Data.DrawBilin = Bridge.DrawBilin; Data.DithMode = Bridge.DithMode; Data.DithThresh = Bridge.DithThresh;
            Data.DrawBorder = Bridge.DrawBorder; Data.Gamma = Bridge.Gamma;
        }
    }
}
