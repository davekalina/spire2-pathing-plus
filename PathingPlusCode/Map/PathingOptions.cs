using Godot;
using System.IO;
using System.Text.Json;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>
/// The mod's settings, live-editable from the map's gear menu and persisted in the
/// game's user data directory. Everything here is display or planning behaviour —
/// nothing touches run state.
/// </summary>
internal static class PathingOptions
{
    /// <summary>
    /// On (the default): routes run all the way to the boss, and pins filter them.
    /// Off: the mod only draws what the player has explicitly asked for — routes
    /// from the current position to the deepest pinned node and no further.
    /// </summary>
    public static bool AutoPath { get; set; } = true;

    /// <summary>Draw pin rings at 75%, to tell them apart from the game's own stamps.</summary>
    public static bool SmallMarkers { get; set; }

    /// <summary>Dash girth across the path; 1.0 is the native dash's own width.</summary>
    public static float DashWidth { get; set; } = 1.9f;

    /// <summary>Dash length along the path, before the per-dash variance.</summary>
    public static float DashLength { get; set; } = 1.6f;

    /// <summary>Extra length the per-dash randomness may add.</summary>
    public static float DashLengthVariance { get; set; } = 0.6f;

    /// <summary>Distance between dash centres. The native connections use 22.</summary>
    public static float DashSpacing { get; set; } = 14f;

    /// <summary>Sideways shift between routes sharing an edge, so parallel runs stay legible.</summary>
    public static float RouteSeparation { get; set; } = 10f;

    /// <summary>Raised when any option changes; the view redraws on it.</summary>
    public static event Action? Changed;

    public static void Notify()
    {
        Save();
        Changed?.Invoke();
    }

    private sealed record Saved(
        bool AutoPath, bool SmallMarkers, float DashWidth, float DashLength,
        float DashLengthVariance, float DashSpacing, float RouteSeparation);

    private static string FilePath =>
        Path.Combine(OS.GetUserDataDir(), "PathingPlus.settings.json");

    public static void Load() => Guard.Run("Loading settings", () =>
    {
        if (!File.Exists(FilePath))
            return;
        if (JsonSerializer.Deserialize<Saved>(File.ReadAllText(FilePath)) is not { } saved)
            return;
        AutoPath = saved.AutoPath;
        SmallMarkers = saved.SmallMarkers;
        DashWidth = saved.DashWidth;
        DashLength = saved.DashLength;
        DashLengthVariance = saved.DashLengthVariance;
        DashSpacing = saved.DashSpacing;
        RouteSeparation = saved.RouteSeparation;
    });

    private static void Save() => Guard.Run("Saving settings", () =>
        File.WriteAllText(FilePath, JsonSerializer.Serialize(new Saved(
            AutoPath, SmallMarkers, DashWidth, DashLength,
            DashLengthVariance, DashSpacing, RouteSeparation))));
}
