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
    public static bool SmallMarkers { get; set; } = true;

    /// <summary>Dash girth across the path; 1.0 is the native dash's own width.</summary>
    public static float DashWidth { get; set; } = 1.7f;

    /// <summary>Dash length along the path, before the per-dash variance.</summary>
    public static float DashLength { get; set; } = 2.7f;

    /// <summary>Extra length the per-dash randomness may add.</summary>
    public static float DashLengthVariance { get; set; } = 1.3f;

    /// <summary>Distance between dash centres. The native connections use 22.</summary>
    public static float DashSpacing { get; set; } = 15f;

    /// <summary>Sideways shift between routes sharing an edge, so parallel runs stay legible.</summary>
    public static float RouteSeparation { get; set; } = 10f;

    /// <summary>Landscape view: the share of the screen width the map is fitted into.</summary>
    public static float LandscapeFit { get; set; } = 0.85f;

    /// <summary>Landscape view: scale applied on top of that fit, to fill the frame.</summary>
    public static float LandscapeZoom { get; set; } = 1.1f;

    /// <summary>Landscape view: horizontal nudge in pixels, positive moves right.</summary>
    public static float LandscapeShiftX { get; set; }

    /// <summary>Landscape view: vertical nudge in pixels, positive moves down.</summary>
    public static float LandscapeShiftY { get; set; }

    /// <summary>
    /// Back to the shipped defaults — the way out for a settings file written before
    /// the defaults moved, which would otherwise mask them forever.
    /// </summary>
    public static void ResetDefaults()
    {
        AutoPath = true;
        SmallMarkers = true;
        DashWidth = 1.7f;
        DashLength = 2.7f;
        DashLengthVariance = 1.3f;
        DashSpacing = 15f;
        RouteSeparation = 10f;
        LandscapeFit = 0.85f;
        LandscapeZoom = 1.1f;
        LandscapeShiftX = 0f;
        LandscapeShiftY = 0f;
    }

    /// <summary>Raised when any option changes; the view redraws on it.</summary>
    public static event Action? Changed;

    public static void Notify()
    {
        Save();
        Changed?.Invoke();
    }

    /// <summary>
    /// Every field nullable on purpose: a settings file written before an option
    /// existed simply omits it, and a null leaves that option at its default rather
    /// than zeroing it — which for a scale or a spacing would break the display.
    /// </summary>
    private sealed class Saved
    {
        public bool? AutoPath { get; set; }
        public bool? SmallMarkers { get; set; }
        public float? DashWidth { get; set; }
        public float? DashLength { get; set; }
        public float? DashLengthVariance { get; set; }
        public float? DashSpacing { get; set; }
        public float? RouteSeparation { get; set; }
        public float? LandscapeFit { get; set; }
        public float? LandscapeZoom { get; set; }
        public float? LandscapeShiftX { get; set; }
        public float? LandscapeShiftY { get; set; }
    }

    private static string FilePath =>
        Path.Combine(OS.GetUserDataDir(), "PathingPlus.settings.json");

    public static void Load() => Guard.Run("Loading settings", () =>
    {
        if (!File.Exists(FilePath))
            return;
        if (JsonSerializer.Deserialize<Saved>(File.ReadAllText(FilePath)) is not { } saved)
            return;
        AutoPath = saved.AutoPath ?? AutoPath;
        SmallMarkers = saved.SmallMarkers ?? SmallMarkers;
        DashWidth = saved.DashWidth ?? DashWidth;
        DashLength = saved.DashLength ?? DashLength;
        DashLengthVariance = saved.DashLengthVariance ?? DashLengthVariance;
        DashSpacing = saved.DashSpacing ?? DashSpacing;
        RouteSeparation = saved.RouteSeparation ?? RouteSeparation;
        LandscapeFit = saved.LandscapeFit ?? LandscapeFit;
        LandscapeZoom = saved.LandscapeZoom ?? LandscapeZoom;
        LandscapeShiftX = saved.LandscapeShiftX ?? LandscapeShiftX;
        LandscapeShiftY = saved.LandscapeShiftY ?? LandscapeShiftY;
    });

    private static void Save() => Guard.Run("Saving settings", () =>
        File.WriteAllText(FilePath, JsonSerializer.Serialize(new Saved
        {
            AutoPath = AutoPath,
            SmallMarkers = SmallMarkers,
            DashWidth = DashWidth,
            DashLength = DashLength,
            DashLengthVariance = DashLengthVariance,
            DashSpacing = DashSpacing,
            RouteSeparation = RouteSeparation,
            LandscapeFit = LandscapeFit,
            LandscapeZoom = LandscapeZoom,
            LandscapeShiftX = LandscapeShiftX,
            LandscapeShiftY = LandscapeShiftY,
        })));
}
