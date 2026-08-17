namespace Voxon
{
    // Audio intentionally DISABLED in the template.  The procedural synth/output
    // was glitchy and has been removed; no audio device is opened.  These are
    // no-op hooks so a new game keeps clean call sites and the Sound / Volume
    // controls — wire up a real audio engine here when you actually need sound.
    internal static class Audio
    {
        public static bool Enabled = true;
        public static float Volume = 0.7f;

        public static void Init() { }
        public static void Dispose() { }

        // generic game hooks (no-op for now)
        public static void Start() { }
        public static void Blip() { }
        public static void Success() { }
        public static void Fail() { }
    }
}
