using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using System.Reflection;

namespace PathingPlus.PathingPlusCode.Map;

/// <summary>
/// The Zoom toggle, upper right: scales <c>TheMap</c> so the whole act fits on screen
/// at once, and back. Scaling happens around a pivot on the screen's horizontal
/// centre line, so the native scroll code — which only ever writes X = 0 into the
/// drag target — cannot yank the view sideways while zoomed. Zooming back in snaps to
/// the current row the same way the screen's own controller code does.
/// </summary>
internal sealed class MapZoom : IDisposable
{
    private static readonly FieldInfo? TargetDragPosField =
        AccessTools.Field(typeof(NMapScreen), "_targetDragPos");
    private static readonly FieldInfo? DistYField =
        AccessTools.Field(typeof(NMapScreen), "_distY");

    private static readonly Color Parchment = new(0.898f, 0.882f, 0.831f);

    private readonly NMapScreen _screen;
    private readonly Control _theMap;
    private readonly Func<IReadOnlyList<Vector2>> _nodeCenters;
    private readonly NinePatchRect _tray;
    private readonly MegaLabel _label;

    public bool Zoomed { get; private set; }

    /// <summary>Raised after the state flips, either direction, any cause.</summary>
    public event Action? Toggled;

    /// <summary>The button, hidden while the map screen itself is closed.</summary>
    public void SetButtonVisible(bool visible) => _tray.Visible = visible;

    public MapZoom(NMapScreen screen, Control theMap, Func<IReadOnlyList<Vector2>> nodeCenters)
    {
        _screen = screen;
        _theMap = theMap;
        _nodeCenters = nodeCenters;

        _tray = new NinePatchRect
        {
            Name = "PathingPlusZoomTray",
            SelfModulate = new Color(0f, 0f, 0f, 0.752941f),
            Texture = ResourceLoader.Load<Texture2D>(
                "res://images/ui/tiny_nine_patch.png", null, ResourceLoader.CacheMode.Reuse),
            PatchMarginLeft = 12,
            PatchMarginTop = 12,
            PatchMarginRight = 12,
            PatchMarginBottom = 12,
        };
        _tray.AnchorLeft = _tray.AnchorRight = 1f;
        _tray.OffsetLeft = -172f;
        _tray.OffsetRight = -48f;
        // Below the top bar AND the debug version/modded readout in the corner.
        _tray.OffsetTop = 196f;
        _tray.OffsetBottom = 252f;
        _tray.GrowHorizontal = Control.GrowDirection.Begin;

        var button = new Control
        {
            Name = "ZoomButton",
            FocusMode = Control.FocusModeEnum.None,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        button.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _tray.AddChild(button);

        _label = new MegaLabel
        {
            AutoSizeEnabled = false,
            Text = "Zoom",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        if (screen.GetNodeOrNull<Label>("MapLegend/Header")?.GetThemeFont("font") is { } font)
            _label.AddThemeFontOverride("font", font);
        _label.AddThemeFontSizeOverride("font_size", 24);
        _label.AddThemeColorOverride("font_color", Parchment);
        _label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.4f));
        _label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        button.AddChild(_label);

        button.GuiInput += inputEvent => Guard.Run("Zoom button", () =>
        {
            if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
                Toggle();
        });

        screen.AddChild(_tray);
    }

    public void Toggle()
    {
        Zoomed = !Zoomed;
        Apply();
        Toggled?.Invoke();
    }

    /// <summary>Back to the standard view — on map change, map close, and map open.</summary>
    public void Reset()
    {
        if (!Zoomed)
            return;
        Zoomed = false;
        Apply();
        Toggled?.Invoke();
    }

    public void Dispose()
    {
        Reset();
        if (GodotObject.IsInstanceValid(_tray))
            _tray.QueueFree();
    }

    private void Apply()
    {
        _label.AddThemeColorOverride("font_color", Zoomed ? StsColors.gold : Parchment);

        if (!Zoomed)
        {
            _theMap.Scale = Vector2.One;
            _theMap.PivotOffset = Vector2.Zero;
            SnapToCurrentRow();
            return;
        }

        var centers = _nodeCenters();
        if (centers.Count == 0)
        {
            Zoomed = false;
            _label.AddThemeColorOverride("font_color", Parchment);
            return;
        }

        var min = centers.Aggregate((a, b) => a.Min(b));
        var max = centers.Aggregate((a, b) => a.Max(b));
        // The boss art towers well above its node centre; give the top extra room.
        min -= new Vector2(160f, 330f);
        max += new Vector2(160f, 150f);
        var extent = max - min;
        var scale = Mathf.Min(1f,
            Mathf.Min((_screen.Size.X - 40f) / extent.X, (_screen.Size.Y - 40f) / extent.Y));

        var centerY = (min.Y + max.Y) * 0.5f;
        _theMap.PivotOffset = new Vector2(_screen.Size.X * 0.5f, centerY);
        _theMap.Scale = Vector2.One * scale;
        SetDragTarget(new Vector2(0f, _screen.Size.Y * 0.5f - centerY));
    }

    private void SnapToCurrentRow()
    {
        var row = RunManager.Instance?.DebugOnlyGetState()?.CurrentMapCoord?.row ?? 0;
        var distY = DistYField?.GetValue(_screen) as float? ?? 155f;
        SetDragTarget(new Vector2(0f, Mathf.Clamp(-600f + row * distY, -600f, 1800f)));
    }

    /// <summary>Move instantly: the target for the lerp and the position itself.</summary>
    private void SetDragTarget(Vector2 target)
    {
        TargetDragPosField?.SetValue(_screen, target);
        _theMap.Position = target;
    }
}
