using PathingPlus.PathingPlusCode.Map;

namespace PathingPlus.PathingPlusCode;

/// <summary>
/// Diagnostics for the parts of this mod that cannot be reasoned about from the
/// outside — drawing strokes arrive as a stream of transformed points, and when the
/// snapping misses there is nothing on screen to say why. Off unless the Debug
/// Logging setting is on, and throttled even then: a stroke is hundreds of points.
/// </summary>
internal static class Diag
{
    private static string _lastLine = "";
    private static int _repeats;

    public static bool Enabled => PathingOptions.DebugLogging;

    /// <summary>Log a line, collapsing an identical line repeated in a row.</summary>
    public static void Log(string line)
    {
        if (!Enabled)
            return;
        if (line == _lastLine)
        {
            // Every tenth repeat, so a stuck state is visible without flooding.
            if (++_repeats % 10 != 0)
                return;
            MainFile.Logger.Info($"[diag] {line} (x{_repeats + 1})");
            return;
        }
        _lastLine = line;
        _repeats = 0;
        MainFile.Logger.Info($"[diag] {line}");
    }
}
