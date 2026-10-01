using System.Collections.Concurrent;

namespace ClickyBot;

internal readonly record struct StaminaBarReading(bool? Visible, int FillPercent);

internal static class ScreenProbe
{
    public const int MaxSearchWidth = 3840;
    public const int MaxSearchHeight = 2160;
}

internal static class ReferenceImageService
{
    public static bool TryLoadRgb(string path, int width, int height, out byte[] rgb)
    {
        rgb = path == "prompt.png" ? new byte[width * height * 3] : [];
        return rgb.Length != 0;
    }
}

internal static class NativeMethods
{
    public static IntPtr GetForegroundWindow() => new(1);
}

internal static class InputSimulator
{
    public static readonly ConcurrentQueue<string> Events = new();
    public static void SendKeyDown(string key) => Events.Enqueue($"down:{key}");
    public static void SendKeyUp(string key) => Events.Enqueue($"up:{key}");
    public static void MoveMouseRelative(int x, int y) => Events.Enqueue($"turn:{x},{y}");
    public static bool ReleaseAllHeldInputs() { Events.Enqueue("release-all"); return true; }
}
