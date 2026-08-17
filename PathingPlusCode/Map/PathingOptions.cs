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
    /// Whether a right-drag on the map picks up the path tool rather than the game's
    /// quill. On by default — planning is what the mod is for, and reaching for the
    /// toolbar every time is the thing a mouse shortcut exists to save — but a player
    /// who wants the map's own scribbling on that button can have it back, and the
    /// path tool is still one press away in the drawing tray.
    ///
    /// It governs that one shortcut and nothing else. Middle-drag stays the eraser,
    /// which rubs out ink and plan alike whatever this says.
    /// </summary>
    public static bool OverrideDrawing { get; set; } = true;

    /// <summary>
    /// Open the map already in the wide view — the whole act on its side, start at
    /// the left and boss at the right — rather than the game's normal view.
    /// </summary>
    public static bool StartWide { get; set; }

    /// <summary>
    /// Whether the path tool leaves ink under the cursor as it draws.
    ///
    /// The mod's stroke is invisible by design — the native line is suppressed and the
    /// route is what appears — which is correct once you trust it and disconcerting
    /// before you do: the pen moves and nothing happens until a node is caught. The
    /// trail answers the stroke immediately, then clears itself.
    /// </summary>
    public static bool DrawingTrail { get; set; } = true;

    /// <summary>Seconds a length of trail takes to go from full ink to nothing.</summary>
    public static float TrailFade { get; set; } = 1f;

    /// <summary>Trail girth. The game's own drawing line is 4.</summary>
    public static float TrailWidth { get; set; } = 4f;

    /// <summary>
    /// How far the pen travels between one point of the trail and the next. These are
    /// points on a polyline rather than nodes, so a small step costs almost nothing and
    /// buys smooth curves; it exists mainly so that a stroke's ink density does not
    /// depend on how fast the mouse reports.
    /// </summary>
    public static float TrailSpacing { get; set; } = 4f;

    /// <summary>
    /// Whether the trail slides onto the map step it is nearest as it fades — the hint
    /// that a stroke is really a run of lines between nodes rather than a scribble.
    /// </summary>
    public static bool TrailSnap { get; set; } = true;

    /// <summary>
    /// How near a map step has to be for the trail to be drawn onto it. Beyond this the
    /// ink simply fades where it was: a length flying across open parchment to reach a
    /// line claims a connection the stroke is not making.
    /// </summary>
    public static float TrailSnapRadius { get; set; } = 150f;

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
    public static float LandscapeFit { get; set; } = 0.95f;

    /// <summary>Landscape view: scale applied on top of that fit, to fill the frame.</summary>
    public static float LandscapeZoom { get; set; } = 1f;

    /// <summary>Landscape view: horizontal nudge in pixels, positive moves right.</summary>
    public static float LandscapeShiftX { get; set; } = 30f;

    /// <summary>Landscape view: vertical nudge in pixels, positive moves down.</summary>
    public static float LandscapeShiftY { get; set; }

    /// <summary>
    /// Back to the shipped defaults — the way out for a settings file written before
    /// the defaults moved, which would otherwise mask them forever.
    /// </summary>
    public static void ResetDefaults()
    {
        OverrideDrawing = true;
        StartWide = false;
        DrawingTrail = true;
        TrailFade = 1f;
        TrailWidth = 4f;
        TrailSpacing = 4f;
        TrailSnap = true;
        TrailSnapRadius = 150f;
        DashWidth = 1.7f;
        DashLength = 2.7f;
        DashLengthVariance = 1.3f;
        DashSpacing = 15f;
        RouteSeparation = 10f;
        LandscapeFit = 0.95f;
        LandscapeZoom = 1f;
        LandscapeShiftX = 30f;
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
        /// <summary>Written when the mod still had three path modes; ignored now.</summary>
        public bool? AutoPath { get; set; }
        public string? Mode { get; set; }

        public bool? OverrideDrawing { get; set; }
        public bool? StartWide { get; set; }
        public bool? DrawingTrail { get; set; }
        public float? TrailFade { get; set; }
        public float? TrailWidth { get; set; }
        public float? TrailSpacing { get; set; }
        public bool? TrailSnap { get; set; }
        public float? TrailSnapRadius { get; set; }
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
        OverrideDrawing = saved.OverrideDrawing ?? OverrideDrawing;
        StartWide = saved.StartWide ?? StartWide;
        DrawingTrail = saved.DrawingTrail ?? DrawingTrail;
        TrailFade = saved.TrailFade ?? TrailFade;
        TrailWidth = saved.TrailWidth ?? TrailWidth;
        TrailSpacing = saved.TrailSpacing ?? TrailSpacing;
        TrailSnap = saved.TrailSnap ?? TrailSnap;
        TrailSnapRadius = saved.TrailSnapRadius ?? TrailSnapRadius;
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
            OverrideDrawing = OverrideDrawing,
            StartWide = StartWide,
            DrawingTrail = DrawingTrail,
            TrailFade = TrailFade,
            TrailWidth = TrailWidth,
            TrailSpacing = TrailSpacing,
            TrailSnap = TrailSnap,
            TrailSnapRadius = TrailSnapRadius,
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
